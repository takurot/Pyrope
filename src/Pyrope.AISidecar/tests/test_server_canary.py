import os
import shutil
import tempfile
import unittest
from unittest.mock import MagicMock

import policy_service_pb2
from server import PolicyService


class TestPolicyServiceCanary(unittest.TestCase):
    def setUp(self):
        self.test_dir = tempfile.mkdtemp()
        self.log_path = os.path.join(self.test_dir, "query_log.jsonl")
        self.service = PolicyService(log_path=self.log_path)

    def tearDown(self):
        shutil.rmtree(self.test_dir)

    def test_report_system_metrics_forwards_latency_to_model_manager(self):
        request = policy_service_pb2.SystemMetricsRequest(
            qps=120.0,
            miss_rate=0.4,
            latency_p99_ms=77.0,
            cpu_utilization=0.8,
            gpu_utilization=0.2,
            cache_hit_total=100,
            cache_miss_total=40,
            timestamp_unix_ms=1,
        )

        self.service._model_manager = MagicMock()
        self.service._model_manager.record_latency_p99.return_value = False
        self.service._bandit_engine.select_action = MagicMock(return_value=0)

        context = MagicMock()
        context.invocation_metadata.return_value = (("tenant-id", "tenant-canary"),)

        self.service.ReportSystemMetrics(request, context)

        self.service._model_manager.record_latency_p99.assert_called_once_with("tenant-canary", 77.0)


if __name__ == "__main__":
    unittest.main()
