import os
import shutil
import tempfile
import unittest
from unittest.mock import MagicMock

from server import PolicyService


class TestPolicyServiceCanary(unittest.TestCase):
    def setUp(self):
        self.test_dir = tempfile.mkdtemp()
        self.log_path = os.path.join(self.test_dir, "query_log.jsonl")
        self.service = PolicyService(log_path=self.log_path)

    def tearDown(self):
        shutil.rmtree(self.test_dir)

    def test_report_system_metrics_forwards_latency_to_model_manager(self):
        request = MagicMock()
        request.tenant_id = "tenant-canary"
        request.qps = 120.0
        request.miss_rate = 0.4
        request.latency_p99_ms = 77.0
        request.cpu_utilization = 0.8
        request.gpu_utilization = 0.2

        self.service._model_manager = MagicMock()
        self.service._model_manager.record_latency_p99.return_value = False
        self.service._bandit_engine.select_action = MagicMock(return_value=0)

        self.service.ReportSystemMetrics(request, MagicMock())

        self.service._model_manager.record_latency_p99.assert_called_once_with("tenant-canary", 77.0)


if __name__ == "__main__":
    unittest.main()
