"""
P6-10: LLM Prefetch Dispatcher
P6-11: LLM Eviction/TTL Overrides

Dispatches LLM outputs to the prefetch queue and applies TTL/eviction advice.
"""

import asyncio
import json
import logging
from typing import Optional, Dict, Any, Callable, Awaitable

logger = logging.getLogger(__name__)

class LLMPrefetchDispatcher:
    """
    P6-10: Converts LLM predictions into prefetch jobs.
    Connects to P6-5 prefetch queue via callback.
    """
    
    def __init__(self, prefetch_callback: Optional[Callable[[str, str, int], Awaitable[None]]] = None):
        """
        Args:
            prefetch_callback: async func(tenant_id, index_name, cluster_id) to queue prefetch
        """
        self.prefetch_callback = prefetch_callback
        self.stats = {
            "dispatched_total": 0,
            "parse_errors": 0,
        }
    
    async def dispatch_prefetch_prediction(self, tenant_id: str, index_name: str, llm_response: str):
        """
        Parse LLM prediction response and dispatch to prefetch queue.
        Expected JSON format: {"prediction": "description", "confidence": 0.8, "cluster_id": 42}
        """
        try:
            data = json.loads(llm_response)
            cluster_id = data.get("cluster_id")
            confidence = data.get("confidence", 0.5)
            
            if cluster_id is not None and confidence > 0.5:
                if self.prefetch_callback:
                    await self.prefetch_callback(tenant_id, index_name, int(cluster_id))
                    self.stats["dispatched_total"] += 1
                    logger.info(f"Prefetch dispatched for cluster {cluster_id} (confidence={confidence})")
                else:
                    logger.warning("No prefetch callback configured")
        except json.JSONDecodeError as e:
            self.stats["parse_errors"] += 1
            logger.error(f"Failed to parse LLM prefetch response: {e}")


class LLMTTLAdvisor:
    """
    P6-11: Applies LLM advisory TTL/priority overrides to cache policy.
    """
    
    def __init__(self, policy_update_callback: Optional[Callable[[str, str, int, int], Awaitable[None]]] = None):
        """
        Args:
            policy_update_callback: async func(tenant_id, index_name, cluster_id, ttl_seconds)
        """
        self.policy_update_callback = policy_update_callback
        self.cluster_overrides: Dict[str, Dict[int, int]] = {}  # index_key -> {cluster_id: ttl}
        self.stats = {
            "overrides_applied": 0,
            "parse_errors": 0,
        }
    
    async def apply_ttl_advice(self, tenant_id: str, index_name: str, llm_response: str):
        """
        Parse LLM TTL advice and apply to policy.
        Expected JSON format: {"action": "shorten", "ttl_seconds": 10, "cluster_id": 42}
        """
        try:
            data = json.loads(llm_response)
            action = data.get("action", "keep")
            cluster_id = data.get("cluster_id")
            ttl_seconds = data.get("ttl_seconds")
            
            index_key = f"{tenant_id}:{index_name}"
            
            if action == "shorten" and cluster_id is not None and ttl_seconds is not None:
                # Store override
                if index_key not in self.cluster_overrides:
                    self.cluster_overrides[index_key] = {}
                self.cluster_overrides[index_key][int(cluster_id)] = int(ttl_seconds)
                self.stats["overrides_applied"] += 1
                logger.info(f"TTL override applied: cluster {cluster_id} -> {ttl_seconds}s")
                
                if self.policy_update_callback:
                    await self.policy_update_callback(tenant_id, index_name, int(cluster_id), int(ttl_seconds))
                    
            elif action == "evict" and cluster_id is not None:
                # Mark for immediate eviction (TTL = 0)
                if index_key not in self.cluster_overrides:
                    self.cluster_overrides[index_key] = {}
                self.cluster_overrides[index_key][int(cluster_id)] = 0
                self.stats["overrides_applied"] += 1
                logger.info(f"Eviction override applied: cluster {cluster_id}")
                
        except json.JSONDecodeError as e:
            self.stats["parse_errors"] += 1
            logger.error(f"Failed to parse LLM TTL advice: {e}")
    
    def get_ttl_override(self, tenant_id: str, index_name: str, cluster_id: int) -> Optional[int]:
        """Get any pending TTL override for a cluster."""
        index_key = f"{tenant_id}:{index_name}"
        overrides = self.cluster_overrides.get(index_key, {})
        return overrides.get(cluster_id)
    
    def clear_overrides(self, tenant_id: str, index_name: str):
        """Clear all overrides for an index."""
        index_key = f"{tenant_id}:{index_name}"
        if index_key in self.cluster_overrides:
            del self.cluster_overrides[index_key]
