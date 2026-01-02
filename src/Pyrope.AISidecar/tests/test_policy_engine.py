import unittest
import os
import sys

sys.path.append(os.path.dirname(os.path.dirname(__file__)))

from policy_engine import HeuristicPolicyEngine  # noqa: E402


class PolicyEngineTests(unittest.TestCase):
    def test_default_policy(self):
        engine = HeuristicPolicyEngine()
        # Normal miss rate
        policy = engine.compute_policy(miss_rate=0.2)
        self.assertEqual(policy.ttl_seconds, 60)
        self.assertEqual(policy.admission_threshold, 0.1)
        self.assertEqual(policy.eviction_priority, 0)

    def test_aggressive_policy(self):
        engine = HeuristicPolicyEngine()
        # High miss rate
        policy = engine.compute_policy(miss_rate=0.6)
        self.assertEqual(policy.ttl_seconds, 300)
        self.assertEqual(policy.admission_threshold, 0.05)
        self.assertEqual(policy.eviction_priority, 1)

    def test_threshold_boundary(self):
        engine = HeuristicPolicyEngine()
        # Edge case: exactly 0.5 should still be default if use > 0.5
        policy = engine.compute_policy(miss_rate=0.5)
        self.assertEqual(policy.ttl_seconds, 60)

        # Slightly above 0.5 should be aggressive
        policy = engine.compute_policy(miss_rate=0.51)
        self.assertEqual(policy.ttl_seconds, 300)


if __name__ == "__main__":
    unittest.main()
