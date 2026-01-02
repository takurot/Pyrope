import unittest
import os
import json
import tempfile
import shutil
from logger import QueryLogger


class TestQueryLogger(unittest.TestCase):
    def setUp(self):
        self.test_dir = tempfile.mkdtemp()
        self.log_path = os.path.join(self.test_dir, "query_log.jsonl")
        self.logger = QueryLogger(self.log_path)

    def tearDown(self):
        shutil.rmtree(self.test_dir)

    def test_log_decision(self):
        tenant_id = "tenant-1"
        query_features = {"norm": 1.0, "topK": 10}
        system_metrics = {"qps": 100.0}
        decision = {"admit": True, "ttl": 60}

        self.logger.log_decision(tenant_id, query_features, system_metrics, decision)

        self.assertTrue(os.path.exists(self.log_path))
        with open(self.log_path, "r") as f:
            lines = f.readlines()
            self.assertEqual(len(lines), 1)
            entry = json.loads(lines[0])
            self.assertEqual(entry["tenant_id"], tenant_id)
            self.assertEqual(entry["query_features"], query_features)
            self.assertEqual(entry["system_metrics"], system_metrics)
            self.assertEqual(entry["decision"], decision)
            self.assertIn("timestamp", entry)


if __name__ == "__main__":
    unittest.main()
