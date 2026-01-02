import os
import sys
import unittest

sys.path.append(os.path.dirname(os.path.dirname(__file__)))

from feature_engineering import (  # noqa: E402
    FeatureEngineer,
    FILTER_TYPE_ENCODING,
    QueryHistory,
    infer_filter_type,
)


class FeatureEngineeringTests(unittest.TestCase):
    def test_infer_filter_type(self):
        self.assertEqual(infer_filter_type(["tag"], None), "tag")
        self.assertEqual(infer_filter_type(None, {"price": 1.0}), "numeric")
        self.assertEqual(infer_filter_type(["tag"], {"price": 1.0}), "hybrid")
        self.assertEqual(infer_filter_type(None, None), "none")

    def test_query_features_norm_and_filter(self):
        engineer = FeatureEngineer()
        features = engineer.extract_query_features([3.0, 4.0], 10, "tag")
        self.assertAlmostEqual(features.norm, 5.0, places=6)
        self.assertEqual(features.top_k, 10.0)
        self.assertEqual(features.filter_type, FILTER_TYPE_ENCODING["tag"])

    def test_system_features_default_queue_depth(self):
        engineer = FeatureEngineer()
        features = engineer.extract_system_features(12.5)
        self.assertEqual(features.qps, 12.5)
        self.assertEqual(features.queue_depth, 0.0)

    def test_history_features_hit_rate_and_revisit_interval(self):
        history = QueryHistory()
        engineer = FeatureEngineer(history)
        history.record("query-a", hit=True, timestamp_ms=1000)

        features = engineer.extract_history_features("query-a", timestamp_ms=1500)
        self.assertEqual(features.hit_rate, 1.0)
        self.assertEqual(features.revisit_interval_ms, 500.0)

        history.record("query-a", hit=False, timestamp_ms=2000)
        features = engineer.extract_history_features("query-a", timestamp_ms=2500)
        self.assertAlmostEqual(features.hit_rate, 0.5, places=6)
        self.assertEqual(features.revisit_interval_ms, 500.0)


if __name__ == "__main__":
    unittest.main()
