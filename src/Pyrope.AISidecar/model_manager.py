import glob
import json
import logging
import os
import shutil
import threading
from collections import deque
from datetime import datetime
from typing import Dict, List, Optional

logger = logging.getLogger(__name__)


class ModelManager:
    def __init__(
        self,
        models_dir="models",
        staging_dir="models/staging",
        active_model_path="models/active.onnx",
        canary_model_path="models/canary.onnx",
        canary_p99_degradation_ratio=1.2,
        canary_min_baseline_samples=10,
        canary_auto_rollback_streak=3,
        canary_baseline_window=100,
    ):
        self.models_dir = models_dir
        self.staging_dir = staging_dir
        self.active_model_path = active_model_path
        self.canary_model_path = canary_model_path
        self.active_version = None
        self.canary_version = None
        self.canary_tenants = set()
        self.lock = threading.Lock()

        self.canary_p99_degradation_ratio = float(canary_p99_degradation_ratio)
        self.canary_min_baseline_samples = int(canary_min_baseline_samples)
        self.canary_auto_rollback_streak = int(canary_auto_rollback_streak)
        self._baseline_p99_samples = deque(maxlen=max(1, int(canary_baseline_window)))
        self._canary_degradation_streak = 0

        # Ensure directories exist
        os.makedirs(self.models_dir, exist_ok=True)
        os.makedirs(self.staging_dir, exist_ok=True)

        self._scan_models()
        self._load_state()

    def _scan_models(self) -> List[Dict]:
        """Scans the models directory for trained models."""
        models = []
        files = glob.glob(os.path.join(self.staging_dir, "*.onnx"))
        for f in files:
            stat = os.stat(f)
            version = os.path.basename(f).replace(".onnx", "")
            created_at = datetime.fromtimestamp(stat.st_mtime).isoformat()

            status = "trained"
            if self.active_version == version:
                status = "active"
            elif self.canary_version == version:
                status = "canary"

            models.append(
                {
                    "version": version,
                    "created_at": created_at,
                    "status": status,
                    "evaluation_score": 0.0,  # TODO: Store evaluations in a sidecar file
                }
            )

        # Sort by creation time desc
        models.sort(key=lambda x: x["created_at"], reverse=True)
        return models

    def list_models(self) -> Dict:
        models = self._scan_models()
        return {
            "models": models,
            "active_model_version": self.active_version or "none",
            "canary_model_version": self.canary_version or "none",
        }

    def train_model(self, dataset_path: Optional[str] = None) -> str:
        """Triggers training and returns a job ID (version)."""
        version = datetime.now().strftime("%Y%m%d_%H%M%S")
        output_path = os.path.join(self.staging_dir, f"{version}.onnx")

        # Run training in a separate thread to avoid blocking API
        # In a real system, this should be a proper job queue
        thread = threading.Thread(target=self._run_training, args=(dataset_path, output_path, version))
        thread.start()

        return version

    def _run_training(self, dataset_path, output_path, version):
        logger.info(f"Starting training for version {version}")
        try:
            # Call the training logic
            # Hack: modify sys.argv if needed or refactor train_model to accept args
            # We will refactor train_model.py to be importable
            import train_model

            # Create a dummy args object or call a function directly
            log_path = dataset_path if dataset_path else "logs/query_log.jsonl"

            logger.info(f"Training on {log_path} -> {output_path}")

            logs = train_model.load_logs(log_path)
            df = train_model.extract_features_and_labels(logs)
            train_model.train_and_export(df, output_path)

            logger.info(f"Training completed for {version}")
        except Exception as e:
            logger.error(f"Training failed for {version}: {e}")

    def deploy_model(self, version: str, canary: bool = False, tenants: List[str] = None) -> str:
        with self.lock:
            return self._deploy_model_locked(version, canary=canary, tenants=tenants)

    def _deploy_model_locked(self, version: str, canary: bool, tenants: Optional[List[str]]) -> str:
        src_path = os.path.join(self.staging_dir, f"{version}.onnx")
        if not os.path.exists(src_path):
            raise ValueError(f"Model version {version} not found")

        if canary:
            shutil.copy2(src_path, self.canary_model_path)
            self.canary_version = version
            self.canary_tenants = set(tenants) if tenants else set()
            self._canary_degradation_streak = 0
            logger.info(f"Deployed {version} as CANARY for tenants: {self.canary_tenants}")
        else:
            shutil.copy2(src_path, self.active_model_path)
            self.active_version = version
            # If promoting canary to active, maybe clear canary?
            if self.canary_version == version:
                self._rollback_canary_locked()
            logger.info(f"Deployed {version} as ACTIVE")

        self._save_state()
        return "OK"

    def is_canary_tenant(self, tenant_id: str) -> bool:
        with self.lock:
            if not self.canary_version:
                return False
            # Empty tenant list means canary applies globally.
            return not self.canary_tenants or tenant_id in self.canary_tenants

    def record_latency_p99(self, tenant_id: str, latency_p99_ms: float) -> bool:
        """Records latency and auto-rolls back canary when degradation persists.

        Returns True when an automatic canary rollback was triggered.
        """
        with self.lock:
            # Keep baseline from non-canary traffic (or all traffic when no canary is active).
            if not self.canary_version or (self.canary_tenants and tenant_id not in self.canary_tenants):
                self._baseline_p99_samples.append(float(latency_p99_ms))
                return False

            if len(self._baseline_p99_samples) < self.canary_min_baseline_samples:
                return False

            baseline = sum(self._baseline_p99_samples) / len(self._baseline_p99_samples)
            threshold = baseline * self.canary_p99_degradation_ratio

            if latency_p99_ms > threshold:
                self._canary_degradation_streak += 1
            else:
                self._canary_degradation_streak = 0

            if self._canary_degradation_streak >= self.canary_auto_rollback_streak:
                logger.warning(
                    "Canary latency degraded (latency_p99_ms=%.3f baseline=%.3f threshold=%.3f). Rolling back canary %s",
                    latency_p99_ms,
                    baseline,
                    threshold,
                    self.canary_version,
                )
                self._rollback_canary_locked()
                self._canary_degradation_streak = 0
                return True

            return False

    def rollback_model(self, canary_only: bool = False) -> str:
        with self.lock:
            if canary_only:
                return self._rollback_canary_locked()

            # Rollback active model... to what?
            # Ideally we track history. For now, let's just say we can't easily rollback active without a version
            # Or we scan for the previous version.
            models = self._scan_models()
            if not models:
                return "No models found"

            # Find current active index
            current_idx = -1
            for i, m in enumerate(models):
                if m["version"] == self.active_version:
                    current_idx = i
                    break

            if current_idx != -1 and current_idx + 1 < len(models):
                prev_version = models[current_idx + 1]["version"]
                logger.info(f"Rolling back active from {self.active_version} to {prev_version}")
                self._deploy_model_locked(prev_version, canary=False, tenants=None)
                return f"Rolled back to {prev_version}"
            return "No previous version found to rollback to"

    def _rollback_canary_locked(self) -> str:
        if self.canary_version:
            logger.info(f"Rolling back canary {self.canary_version}")
            self.canary_version = None
            self.canary_tenants = set()
            if os.path.exists(self.canary_model_path):
                os.remove(self.canary_model_path)
            self._save_state()
            return "OK"
        return "No canary to rollback"

    def _save_state(self):
        state = {
            "active_version": self.active_version,
            "canary_version": self.canary_version,
            "canary_tenants": list(self.canary_tenants),
        }
        with open(os.path.join(self.models_dir, "state.json"), "w") as f:
            json.dump(state, f)

    def _load_state(self):
        state_path = os.path.join(self.models_dir, "state.json")
        if os.path.exists(state_path):
            with open(state_path, "r") as f:
                state = json.load(f)
                self.active_version = state.get("active_version")
                self.canary_version = state.get("canary_version")
                self.canary_tenants = set(state.get("canary_tenants", []))
