from __future__ import annotations
from dataclasses import dataclass


@dataclass(frozen=True)
class PolicyConfig:
    admission_threshold: float
    ttl_seconds: int
    eviction_priority: int


class HeuristicPolicyEngine:
    def __init__(self):
        # Default policy
        self._default_policy = PolicyConfig(admission_threshold=0.1, ttl_seconds=60, eviction_priority=0)
        # Aggressive policy
        self._aggressive_policy = PolicyConfig(admission_threshold=0.05, ttl_seconds=300, eviction_priority=1)

    def compute_policy(self, miss_rate: float) -> PolicyConfig:
        """
        Simple heuristic: If miss rate is high (> 0.5), switch to aggressive caching policy.
        """
        if miss_rate > 0.5:
            return self._aggressive_policy
        return self._default_policy
