import asyncio
import logging
import os
import sys
import threading
import time
import warnings
from concurrent import futures

import grpc

# These imports will work after running codegen.py
try:
    import policy_service_pb2
    import policy_service_pb2_grpc
except ImportError as e:
    print(f"DEBUG: Failed to import protobufs: {e}", flush=True)
    sys.exit(1)

from feature_engineering import FeatureEngineer
from llm_policy_engine import LLMPolicyEngine, SystemMetrics
from llm_worker import LLMWorker
from logger import QueryLogger
from policy_engine import HeuristicPolicyEngine
from prediction_engine import PredictionEngine
from model_manager import ModelManager
from bandit_engine import ContextualBanditEngine

# Suppress google.generativeai deprecation warning for clean demo output
warnings.filterwarnings("ignore", category=FutureWarning, module="google.generativeai")

# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s - %(name)s - %(levelname)s - %(message)s",
    force=True,
    handlers=[logging.StreamHandler(sys.stdout)],
)
logger = logging.getLogger(__name__)
logger.info("Logging configured successfully")

# Feature flag for Gemini-based cache control
LLM_POLICY_ENABLED = os.getenv("LLM_POLICY_ENABLED", "false").lower() == "true"


class PolicyService(policy_service_pb2_grpc.PolicyServiceServicer):
    def __init__(self, log_path="logs/query_log.jsonl"):
        self._feature_engineer = FeatureEngineer()
        self._heuristic_engine = HeuristicPolicyEngine()
        self._prediction_engine = PredictionEngine()
        self._logger = QueryLogger(log_path)
        self._latest_system_features = None
        self._llm_worker = LLMWorker()  # Initialize LLM Worker
        self._event_loop = None  # Will be set when async loop starts

        self._model_manager = ModelManager()
        self._bandit_engine = ContextualBanditEngine()

        # P6-13: LLMPolicyEngine with fallback to heuristic
        if LLM_POLICY_ENABLED:
            self._llm_policy_engine = LLMPolicyEngine(
                llm_worker=self._llm_worker,
                fallback=self._heuristic_engine,
            )
            print("LLM Policy Engine ENABLED (Gemini-based cache control)")
        else:
            self._llm_policy_engine = None
            print("LLM Policy Engine DISABLED (using heuristic)")

        # Start background training
        self._training_thread = threading.Thread(target=self._training_loop, daemon=True)
        self._training_thread.start()

    def start_background_services(self):
        asyncio.run(self._llm_worker.start())

    def stop_background_services(self):
        asyncio.run(self._llm_worker.stop())

    def _training_loop(self):
        while True:
            try:
                self._prediction_engine.train_model()
            except Exception as e:
                print(f"Error in training loop: {e}")
            time.sleep(60)

    def GetIndexPolicy(self, request, context):
        print(f"Received request for tenant: {request.tenant_id}, index: {request.index_name}")
        # P8-5: Check if tenant is canary
        if request.tenant_id in self._model_manager.canary_tenants:
            print(f"Tenant {request.tenant_id} routed to CANARY model {self._model_manager.canary_version}")
            # TODO: Load params from canary model config if available

        return policy_service_pb2.IndexPolicyResponse(pq_m=16, pq_construction=200, pca_dimension=64, status="OK")

    def ReportSystemMetrics(self, request, context):
        self._latest_system_features = self._feature_engineer.extract_system_features(request.qps, queue_depth=None)

        # P8-6: Online Learning Loop
        # 1. Update Bandit with previous reward (Change in miss rate?)
        # For simplicity, we just assume improvement is reward.
        # But we need state from PREVIOUS step.
        # Here we just use current state to predict Action.

        bandit_features = self._bandit_engine.get_features(request)
        action = self._bandit_engine.select_action(bandit_features)

        # Action 0: Normal (Heuristic/LLM), Action 1: Aggressive Override

        policy_config = None

        # P6-13: Use LLM or heuristic based on feature flag
        if self._llm_policy_engine and self._event_loop:
            # Async LLM path
            metrics = SystemMetrics(
                qps=request.qps,
                miss_rate=request.miss_rate,
                latency_p99_ms=request.latency_p99_ms,
                cpu_utilization=request.cpu_utilization,
                gpu_utilization=request.gpu_utilization,
            )
            future = asyncio.run_coroutine_threadsafe(self._llm_policy_engine.compute_policy(metrics), self._event_loop)
            try:
                policy_config = future.result(timeout=5.0)
            except Exception as e:
                print(f"LLM policy error: {e}, falling back to heuristic")
                policy_config = self._heuristic_engine.compute_policy(request.miss_rate)
        else:
            # Heuristic path
            policy_config = self._heuristic_engine.compute_policy(request.miss_rate)

        # Apply Bandit Override (Action 1 = Aggressive)
        if action == 1:
            policy_config.ttl_seconds = max(10, policy_config.ttl_seconds // 2)
            policy_config.admission_threshold = max(0.0, policy_config.admission_threshold - 0.1)
            # print("BANDIT: Applied Aggressive override")

        # Fake Reward Calculation (minimize miss rate)
        # Positive reward for low miss rate; negative for high miss rate.
        baseline = 0.3
        reward = baseline - request.miss_rate
        reward = max(-1.0, min(1.0, reward))
        self._bandit_engine.update(bandit_features, action, reward)

        print(
            "Metrics: "
            f"qps={request.qps:.2f} "
            f"miss_rate={request.miss_rate:.2f} "
            f"latency_p99_ms={request.latency_p99_ms:.2f} "
            f"cpu={request.cpu_utilization:.2f} "
            f"gpu={request.gpu_utilization:.2f} -> "
            f"Policy(ttl={policy_config.ttl_seconds}) [BanditAction={action}]"
        )

        policy_proto = policy_service_pb2.WarmPathPolicy(
            admission_threshold=policy_config.admission_threshold,
            ttl_seconds=policy_config.ttl_seconds,
            eviction_priority=policy_config.eviction_priority,
        )

        # Log decision for offline datagen
        # Use latest features if available
        query_features = {}  # We don't have per-query features in ReportSystemMetrics yet
        system_metrics = {
            "qps": request.qps,
            "miss_rate": request.miss_rate,
            "latency_p99_ms": request.latency_p99_ms,
            "cpu_utilization": request.cpu_utilization,
            "gpu_utilization": request.gpu_utilization,
        }
        decision = {
            "admission_threshold": policy_config.admission_threshold,
            "ttl_seconds": policy_config.ttl_seconds,
            "eviction_priority": policy_config.eviction_priority,
            "bandit_action": int(action)
        }
        tenant_id = getattr(request, "tenant_id", "system")
        self._logger.log_decision(tenant_id, query_features, system_metrics, decision)

        return policy_service_pb2.SystemMetricsResponse(status="OK", next_report_interval_ms=0, policy=policy_proto)

    def ReportClusterAccess(self, request, context):
        for access in request.accesses:
            self._prediction_engine.record_interaction(request.tenant_id, request.index_name, access.cluster_id)
        return policy_service_pb2.ReportClusterAccessResponse(status="OK")

    def GetPrefetchRules(self, request, context):
        # Rules are updated by background thread
        rules_map = self._prediction_engine.rules.get(f"{request.tenant_id}:{request.index_name}", {})

        response_rules = []
        for current_id, next_id in rules_map.items():
            response_rules.append(
                policy_service_pb2.PrefetchRule(current_cluster_id=current_id, next_cluster_id=next_id)
            )

        return policy_service_pb2.GetPrefetchRulesResponse(rules=response_rules)

    # --- P8-4 Model Management RPCs ---

    def ListModels(self, request, context):
        data = self._model_manager.list_models()
        return policy_service_pb2.ModelList(
            models=[policy_service_pb2.ModelInfo(**m) for m in data["models"]],
            active_model_version=data["active_model_version"],
            canary_model_version=data["canary_model_version"]
        )

    def TrainModel(self, request, context):
        job_id = self._model_manager.train_model(request.dataset_path)
        return policy_service_pb2.TrainResponse(status="Started", job_id=job_id)

    def DeployModel(self, request, context):
        try:
            self._model_manager.deploy_model(request.version, request.canary, list(request.canary_tenants))
            return policy_service_pb2.DeployResponse(status="OK", version=request.version)
        except ValueError as e:
            return policy_service_pb2.DeployResponse(status=f"Error: {e}", version="")

    def RollbackModel(self, request, context):
        status = self._model_manager.rollback_model(request.canary_only)
        active = self._model_manager.active_version or "none"
        return policy_service_pb2.RollbackResponse(status=status, active_version=active)

    def GetEvaluations(self, request, context):
        return policy_service_pb2.EvaluationMetrics(
            current_p99_improvement=0.25,  # Placeholder from evaluate_model.py logic
            current_cache_hit_rate=0.85,
            other_metrics={"bandit_epsilon": self._bandit_engine.epsilon}
        )


def _read_file_bytes(path: str) -> bytes:
    with open(path, "rb") as f:
        return f.read()


def _parse_bool_env(name: str, default: bool = False) -> bool:
    raw = os.getenv(name)
    if raw is None:
        return default
    return raw.strip().lower() in ("1", "true", "yes", "on")


def _configure_ports(server: grpc.Server, port: int) -> None:
    mtls_enabled = _parse_bool_env("PYROPE_SIDECAR_MTLS_ENABLED", default=False)

    if not mtls_enabled:
        server.add_insecure_port(f"[::]:{port}")
        return

    cert_pem = os.getenv("PYROPE_SIDECAR_CERT_PEM")
    key_pem = os.getenv("PYROPE_SIDECAR_KEY_PEM")
    ca_pem = os.getenv("PYROPE_SIDECAR_CA_CERT_PEM")
    if not cert_pem or not key_pem or not ca_pem:
        raise ValueError("mTLS enabled but PYROPE_SIDECAR_CERT_PEM/KEY_PEM/CA_CERT_PEM are not configured")

    server_cert_chain = _read_file_bytes(cert_pem)
    server_private_key = _read_file_bytes(key_pem)
    root_certificates = _read_file_bytes(ca_pem)

    creds = grpc.ssl_server_credentials(
        [(server_private_key, server_cert_chain)],
        root_certificates=root_certificates,
        require_client_auth=True,
    )
    server.add_secure_port(f"[::]:{port}", creds)


def serve():
    port = int(os.getenv("PYROPE_SIDECAR_PORT", "50051"))
    server = grpc.server(futures.ThreadPoolExecutor(max_workers=10))

    print("DEBUG: Initializing PolicyService...", flush=True)
    try:
        policy_service = PolicyService()
        print("DEBUG: PolicyService initialized successfully.", flush=True)
    except Exception as e:
        print(f"CRITICAL ERROR initializing PolicyService: {e}", flush=True)
        import traceback

        traceback.print_exc()
        sys.exit(1)

    policy_service_pb2_grpc.add_PolicyServiceServicer_to_server(policy_service, server)

    _configure_ports(server, port)

    # FIX: Proper async loop management for LLM Worker
    # Create loop and keep reference for clean shutdown
    loop = asyncio.new_event_loop()
    llm_worker = policy_service._llm_worker

    # P6-13: Pass event loop to PolicyService for LLM async calls
    policy_service._event_loop = loop

    def run_async_loop():
        asyncio.set_event_loop(loop)
        loop.run_until_complete(llm_worker.start())
        loop.run_forever()

    async_thread = threading.Thread(target=run_async_loop, daemon=True)
    async_thread.start()

    print(
        f"Starting AI Sidecar server on port {port} (mTLS={'on' if _parse_bool_env('PYROPE_SIDECAR_MTLS_ENABLED') else 'off'})..."
    )
    server.start()
    try:
        while True:
            time.sleep(86400)
    except KeyboardInterrupt:
        print("Shutting down...")
        # FIX: Properly stop LLM worker using thread-safe call
        if llm_worker and loop.is_running():
            future = asyncio.run_coroutine_threadsafe(llm_worker.stop(), loop)
            try:
                future.result(timeout=5.0)
            except Exception as e:
                print(f"LLMWorker stop error: {e}")
            # Stop the event loop
            loop.call_soon_threadsafe(loop.stop)
        server.stop(0)
        print("AI Sidecar stopped.")


if __name__ == "__main__":
    serve()
