import grpc
from concurrent import futures
import time

# These imports will work after running codegen.py
import policy_service_pb2
import policy_service_pb2_grpc


class PolicyService(policy_service_pb2_grpc.PolicyServiceServicer):
    def GetIndexPolicy(self, request, context):
        print(f"Received request for tenant: {request.tenant_id}, index: {request.index_name}")
        return policy_service_pb2.IndexPolicyResponse(pq_m=16, pq_construction=200, pca_dimension=64, status="OK")

    def ReportSystemMetrics(self, request, context):
        print(
            "Metrics: "
            f"qps={request.qps:.2f} "
            f"miss_rate={request.miss_rate:.2f} "
            f"latency_p99_ms={request.latency_p99_ms:.2f} "
            f"cpu={request.cpu_utilization:.2f} "
            f"gpu={request.gpu_utilization:.2f}"
        )
        return policy_service_pb2.SystemMetricsResponse(status="OK", next_report_interval_ms=0)


def serve():
    server = grpc.server(futures.ThreadPoolExecutor(max_workers=10))
    policy_service_pb2_grpc.add_PolicyServiceServicer_to_server(PolicyService(), server)
    server.add_insecure_port("[::]:50051")
    print("Starting AI Sidecar server on port 50051...")
    server.start()
    try:
        while True:
            time.sleep(86400)
    except KeyboardInterrupt:
        server.stop(0)


if __name__ == "__main__":
    serve()
