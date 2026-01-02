import unittest
import os
import json
import tempfile
import shutil
from unittest.mock import MagicMock
import policy_service_pb2
from server import PolicyService


class TestPolicyServiceLogging(unittest.TestCase):
    def setUp(self):
        self.test_dir = tempfile.mkdtemp()
        self.log_path = os.path.join(self.test_dir, "query_log.jsonl")
        self.service = PolicyService(log_path=self.log_path)

    def tearDown(self):
        shutil.rmtree(self.test_dir)

    def test_report_system_metrics_logs_decision(self):
        request = MagicMock(spec=policy_service_pb2.SystemMetricsRequest)
        request.tenant_id = "tenant-1"
        request.qps = 100.0
        request.miss_rate = 0.2
        request.latency_p99_ms = 10.0
        request.cpu_utilization = 0.5
        request.gpu_utilization = 0.1

        context = MagicMock()

        self.service.ReportSystemMetrics(request, context)

        self.assertTrue(os.path.exists(self.log_path))
        with open(self.log_path, "r") as f:
            lines = f.readlines()
            self.assertEqual(len(lines), 1)
            entry = json.loads(lines[0])
            self.assertEqual(entry["tenant_id"], "tenant-1")
            self.assertEqual(entry["system_metrics"]["qps"], 100.0)
            self.assertIn("decision", entry)


if __name__ == "__main__":
    unittest.main()
