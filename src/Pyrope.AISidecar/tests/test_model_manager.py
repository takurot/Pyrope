import os
import shutil
import tempfile
import unittest

from model_manager import ModelManager


class TestModelManagerCanary(unittest.TestCase):
    def setUp(self):
        self.test_dir = tempfile.mkdtemp()
        self.models_dir = os.path.join(self.test_dir, "models")
        self.staging_dir = os.path.join(self.models_dir, "staging")
        os.makedirs(self.staging_dir, exist_ok=True)

        self.active_model_path = os.path.join(self.models_dir, "active.onnx")
        self.canary_model_path = os.path.join(self.models_dir, "canary.onnx")

        self._write_model("v1")
        self._write_model("v2")

        self.manager = ModelManager(
            models_dir=self.models_dir,
            staging_dir=self.staging_dir,
            active_model_path=self.active_model_path,
            canary_model_path=self.canary_model_path,
            canary_p99_degradation_ratio=1.2,
            canary_min_baseline_samples=3,
            canary_auto_rollback_streak=2,
        )

    def tearDown(self):
        shutil.rmtree(self.test_dir)

    def _write_model(self, version: str):
        path = os.path.join(self.staging_dir, f"{version}.onnx")
        with open(path, "wb") as f:
            f.write(b"dummy")

    def test_deploy_canary_sets_target_tenants(self):
        status = self.manager.deploy_model("v2", canary=True, tenants=["tenant-a", "tenant-b"])

        self.assertEqual("OK", status)
        self.assertEqual("v2", self.manager.canary_version)
        self.assertSetEqual({"tenant-a", "tenant-b"}, self.manager.canary_tenants)
        self.assertTrue(os.path.exists(self.canary_model_path))

    def test_record_latency_auto_rolls_back_on_p99_degradation(self):
        self.manager.deploy_model("v1", canary=False)

        self.assertFalse(self.manager.record_latency_p99("tenant-control", 20.0))
        self.assertFalse(self.manager.record_latency_p99("tenant-control", 22.0))
        self.assertFalse(self.manager.record_latency_p99("tenant-control", 21.0))

        self.manager.deploy_model("v2", canary=True, tenants=["tenant-canary"])

        self.assertFalse(self.manager.record_latency_p99("tenant-canary", 31.0))
        self.assertTrue(self.manager.record_latency_p99("tenant-canary", 32.0))
        self.assertIsNone(self.manager.canary_version)
        self.assertSetEqual(set(), self.manager.canary_tenants)
        self.assertFalse(os.path.exists(self.canary_model_path))


if __name__ == "__main__":
    unittest.main()
