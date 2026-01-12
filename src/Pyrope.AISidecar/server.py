import grpc
from concurrent import futures
import time
import os

# These imports will work after running codegen.py
import policy_service_pb2
import policy_service_pb2_grpc

from feature_engineering import FeatureEngineer
from policy_engine import HeuristicPolicyEngine
from logger import QueryLogger
from prediction_engine import PredictionEngine


class PolicyService(policy_service_pb2_grpc.PolicyServiceServicer):
    def __init__(self, log_path="logs/query_log.jsonl"):
        self._feature_engineer = FeatureEngineer()
        self._policy_engine = HeuristicPolicyEngine()
        self._prediction_engine = PredictionEngine()
        self._logger = QueryLogger(log_path)
        self._latest_system_features = None

    def GetIndexPolicy(self, request, context):
        print(f"Received request for tenant: {request.tenant_id}, index: {request.index_name}")
        return policy_service_pb2.IndexPolicyResponse(pq_m=16, pq_construction=200, pca_dimension=64, status="OK")

    def ReportSystemMetrics(self, request, context):
        self._latest_system_features = self._feature_engineer.extract_system_features(request.qps, queue_depth=None)

        # Compute policy based on miss_rate
        policy_config = self._policy_engine.compute_policy(request.miss_rate)

        print(
            "Metrics: "
            f"qps={request.qps:.2f} "
            f"miss_rate={request.miss_rate:.2f} "
            f"latency_p99_ms={request.latency_p99_ms:.2f} "
            f"cpu={request.cpu_utilization:.2f} "
            f"gpu={request.gpu_utilization:.2f} -> "
            f"Policy(ttl={policy_config.ttl_seconds})"
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
        }
        tenant_id = getattr(request, "tenant_id", "system")
        self._logger.log_decision(tenant_id, query_features, system_metrics, decision)

        return policy_service_pb2.SystemMetricsResponse(status="OK", next_report_interval_ms=0, policy=policy_proto)

    def ReportClusterAccess(self, request, context):
        for access in request.accesses:
            self._prediction_engine.record_interaction(request.tenant_id, request.index_name, access.cluster_id)
        return policy_service_pb2.ReportClusterAccessResponse(status="OK")

    def GetPrefetchRules(self, request, context):
        # Trigger training to ensure rules are up to date
        # In production this might be async/throttled
        self._prediction_engine.train_model()

        rules_map = self._prediction_engine.rules.get(f"{request.tenant_id}:{request.index_name}", {})

        response_rules = []
        for current_id, next_id in rules_map.items():
            response_rules.append(
                policy_service_pb2.PrefetchRule(current_cluster_id=current_id, next_cluster_id=next_id)
            )

        return policy_service_pb2.GetPrefetchRulesResponse(rules=response_rules)


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
    policy_service_pb2_grpc.add_PolicyServiceServicer_to_server(PolicyService(), server)
    _configure_ports(server, port)
    print(
        f"Starting AI Sidecar server on port {port} (mTLS={'on' if _parse_bool_env('PYROPE_SIDECAR_MTLS_ENABLED') else 'off'})..."
    )
    server.start()
    try:
        while True:
            time.sleep(86400)
    except KeyboardInterrupt:
        server.stop(0)


if __name__ == "__main__":
    serve()
