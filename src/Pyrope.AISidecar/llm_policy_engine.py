"""
LLMPolicyEngine: Gemini-based Cache Policy Decisions (P6-13)

Replaces HeuristicPolicyEngine with LLM-driven TTL/admission decisions.
Falls back to heuristic on LLM failure/timeout.

Refactored to meet "50ms strict timeout" by using an async update pattern.
"""

from __future__ import annotations

import asyncio
import json
import logging
import re
import time
from dataclasses import dataclass
from typing import Optional, Set

from policy_engine import HeuristicPolicyEngine, PolicyConfig

logger = logging.getLogger(__name__)


@dataclass
class SystemMetrics:
    """System metrics for policy decision."""

    qps: float = 0.0
    miss_rate: float = 0.0
    latency_p99_ms: float = 0.0
    cpu_utilization: float = 0.0
    gpu_utilization: float = 0.0


class LLMPolicyEngine:
    """
    LLM-based cache policy engine using Gemini.

    Features:
    - Generates prompts from system metrics
    - Parses structured JSON responses
    - Caches decisions to reduce API calls
    - Falls back to HeuristicPolicyEngine on failure
    - [New] Async non-blocking updates (returns Heuristic/Stale while updating)
    - [New] Input validation and clamping
    - [New] In-flight deduplication
    """

    PROMPT_TEMPLATE = """You are an autonomous controller for a vector database cache.
Your goal is to optimize for the following priorities:
1. Stability: Keep P99 latency under 50ms.
2. Efficiency: Maximize cache hit rate to reduce expensive vector search computations.
3. Resource Management: Prevent CPU saturation (target < 80% utilization).

System Metrics:
- Current QPS: {qps}
- Current Cache Miss Rate: {miss_rate}
- P99 Latency: {latency_p99_ms}ms
- CPU Utilization: {cpu_utilization}%
- GPU Utilization: {gpu_utilization}%

Task:
Determine the optimal cache configuration to balance these goals based on the provided metrics.
If resources are tight, sacrifice some cache efficiency to maintain stability.
If latency is low and resources are available, try to increase TTL to improve future hit rates.
If miss rate is high, consider whether increasing TTL or admission selectivity is better.

Respond ONLY with a valid JSON object in this exact format:
{{"ttl_seconds": <int between 30 and 3600>, "admission_threshold": <float 0-1>, "eviction_priority": <int 0-2>, "reasoning": "<short explanation>"}}
"""

    def __init__(
        self,
        llm_worker,
        fallback: HeuristicPolicyEngine,
        timeout_seconds: float = 5.0, # Kept for API timeout logic, not blocking wait
        cache_ttl_seconds: float = 60.0,
    ):
        self._llm = llm_worker
        self._fallback = fallback
        self._timeout = timeout_seconds
        self._cache_ttl = cache_ttl_seconds

        # Decision cache: (metrics_key) -> (PolicyConfig, timestamp)
        self._cache: dict[str, tuple[PolicyConfig, float]] = {}
        
        # Deduplication set for keys currently being processed by LLM
        self._inflight_keys: Set[str] = set()

    def _build_prompt(self, metrics: SystemMetrics) -> str:
        """Build prompt from system metrics."""
        return self.PROMPT_TEMPLATE.format(
            qps=metrics.qps,
            miss_rate=metrics.miss_rate,
            latency_p99_ms=metrics.latency_p99_ms,
            cpu_utilization=metrics.cpu_utilization,
            gpu_utilization=metrics.gpu_utilization,
        )
    
    def _validate_and_clamp(self, config: PolicyConfig) -> PolicyConfig:
        """
        [Review] Validate and clamp policy values to safe ranges.
        """
        # Clamp TTL (30s - 3600s)
        ttl = max(30, min(3600, config.ttl_seconds))
        
        # Clamp Admission (0.0 - 1.0)
        admission = max(0.0, min(1.0, config.admission_threshold))
        
        # Clamp Eviction Priority (0 - 2)
        eviction = max(0, min(2, config.eviction_priority))
        
        return PolicyConfig(
            ttl_seconds=int(ttl),
            admission_threshold=float(admission),
            eviction_priority=int(eviction)
        )

    def _parse_response(self, response: str) -> Optional[PolicyConfig]:
        """Parse LLM response into PolicyConfig."""
        if not response:
            return None

        try:
            # Try to extract JSON from response (may have surrounding text)
            json_match = re.search(r"\{[^}]+\}", response)
            if not json_match:
                logger.warning(f"No JSON found in LLM response: {response[:100]}")
                return None

            data = json.loads(json_match.group())

            # Validate required fields
            required = ["ttl_seconds", "admission_threshold", "eviction_priority"]
            if not all(k in data for k in required):
                logger.warning(f"Missing required fields in LLM response: {data}")
                return None

            config = PolicyConfig(
                admission_threshold=float(data["admission_threshold"]),
                ttl_seconds=int(data["ttl_seconds"]),
                eviction_priority=int(data["eviction_priority"]),
            )
            
            # [Review] Validate semantics
            return self._validate_and_clamp(config)

        except (json.JSONDecodeError, ValueError, TypeError) as e:
            logger.warning(f"Failed to parse LLM response: {e}, response: {response[:100]}")
            return None

    def _get_cache_key(self, metrics: SystemMetrics) -> str:
        """Generate cache key from metrics."""
        qps_bucket = int(metrics.qps / 10) * 10
        miss_bucket = round(metrics.miss_rate, 1)
        latency_bucket = int(metrics.latency_p99_ms / 10) * 10
        cpu_bucket = int(metrics.cpu_utilization / 10) * 10
        # [Review] Added GPU metrics to key
        gpu_bucket = int(metrics.gpu_utilization / 10) * 10
        return f"{qps_bucket}:{miss_bucket}:{latency_bucket}:{cpu_bucket}:{gpu_bucket}"

    def _get_cached(self, metrics: SystemMetrics) -> Optional[PolicyConfig]:
        """Get cached decision if still valid."""
        key = self._get_cache_key(metrics)
        if key in self._cache:
            config, timestamp = self._cache[key]
            # Use strict TTL for now, but we could return stale data if needed
            if time.time() - timestamp < self._cache_ttl:
                return config
            else:
                del self._cache[key]
        return None

    def _set_cache(self, metrics: SystemMetrics, config: PolicyConfig):
        """Cache decision."""
        key = self._get_cache_key(metrics)
        self._cache[key] = (config, time.time())

    async def compute_policy(self, metrics: SystemMetrics) -> PolicyConfig:
        """
        Compute cache policy using LLM.
        
        [Review] Non-blocking implementation:
        1. Checks cache -> returns if Hit
        2. Checks inflight -> returns Heuristic if already processing
        3. If Cache Miss & No Inflight -> Launches Async Job & Returns Heuristic immediately.
        
        This satisfies the strict latency requirements of the metrics reporting loop.
        """
        # 1. Check cache first
        cached = self._get_cached(metrics)
        if cached:
            return cached

        key = self._get_cache_key(metrics)

        # 2. Check if already in flight (Deduplication)
        if key in self._inflight_keys:
            # Already working on it, return heuristic/fallback for now to keep things fast
            return self._fallback.compute_policy(metrics.miss_rate)

        # 3. Launch async update (Fire and Forget)
        self._inflight_keys.add(key)
        
        # Capture context for callback
        current_metrics = metrics
        
        async def on_complete(response_text):
            try:
                self._inflight_keys.discard(key)
                if response_text:
                    config = self._parse_response(response_text)
                    if config:
                        logger.info(f"LLM Policy Updated for {key}: {config}")
                        self._set_cache(current_metrics, config)
                    else:
                        logger.warning(f"Failed to parse LLM response for {key}")
                else:
                     logger.warning(f"LLM returned None for {key}")
            except Exception as e:
                logger.error(f"Error in LLM callback: {e}")
                self._inflight_keys.discard(key)

        prompt = self._build_prompt(metrics)
        success = await self._llm.submit_task(prompt, callback=on_complete)
        
        if success:
             logger.info(f"Triggered async LLM update for key {key}")
        else:
             self._inflight_keys.discard(key)
             logger.warning(f"Failed to submit LLM task for {key}")

        # Return heuristic immediately so we don't block the Sidecar server
        return self._fallback.compute_policy(metrics.miss_rate)
