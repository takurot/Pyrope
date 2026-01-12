
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


if __name__ == '__main__':
    unittest.main()
