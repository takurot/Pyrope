import unittest
from prediction_engine import PredictionEngine


class TestPredictionEngine(unittest.TestCase):
    def test_training(self):
        engine = PredictionEngine()
        # A -> B
        engine.record_interaction("t1", "i1", 1)
        engine.record_interaction("t1", "i1", 2)
        # B -> C
        engine.record_interaction("t1", "i1", 3)
        # A -> B
        engine.record_interaction("t1", "i1", 1)
        engine.record_interaction("t1", "i1", 2)
        # A -> B
        engine.record_interaction("t1", "i1", 1)
        engine.record_interaction("t1", "i1", 2)

        # (1->2) x 3 times. Rules threshold is 3.

        engine.train_model()

        pred = engine.get_prediction("t1", "i1", 1)
        self.assertEqual(pred, 2)

        pred_bad = engine.get_prediction("t1", "i1", 2)  # 2 -> 3 only once
        self.assertEqual(pred_bad, -1)

    def test_pruning(self):
        # Initialize with small limits to trigger pruning
        engine = PredictionEngine(max_tenants=2, max_clusters_per_tenant=2)

        # Add 3 tenants
        engine.record_interaction("t1", "i1", 1)
        engine.record_interaction("t2", "i1", 1)
        engine.record_interaction("t3", "i1", 1)

        # Check that we have at most 2 tenants (plus maybe leftovers depending on pruning logic)
        # The pruning logic removes 10% or at least 1.
        # With 3 added, 1 should be removed -> 2 remaining.
        self.assertLessEqual(len(engine.transitions), 2)

        # Test cluster pruning
        # Add 3 clusters for t4
        engine.record_interaction("t4", "i1", 1)
        engine.record_interaction("t4", "i1", 2)  # 1->2
        engine.record_interaction("t4", "i1", 3)  # 2->3
        engine.record_interaction("t4", "i1", 4)  # 3->4

        key = "t4:i1"
        if key in engine.transitions:
            self.assertLessEqual(len(engine.transitions[key]), 2)


if __name__ == "__main__":
    unittest.main()
