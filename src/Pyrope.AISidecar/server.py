import grpc
from concurrent import futures
import time

# These imports will work after running codegen.py
import policy_service_pb2
import policy_service_pb2_grpc

class PolicyService(policy_service_pb2_grpc.PolicyServiceServicer):
    def GetIndexPolicy(self, request, context):
        print(f"Received request for tenant: {request.tenant_id}, index: {request.index_name}")
        return policy_service_pb2.IndexPolicyResponse(
            pq_m=16,
            pq_construction=200,
            pca_dimension=64,
            status="OK"
        )

def serve():
    server = grpc.server(futures.ThreadPoolExecutor(max_workers=10))
    policy_service_pb2_grpc.add_PolicyServiceServicer_to_server(PolicyService(), server)
    server.add_insecure_port('[::]:50051')
    print("Starting AI Sidecar server on port 50051...")
    server.start()
    try:
        while True:
            time.sleep(86400)
    except KeyboardInterrupt:
        server.stop(0)

if __name__ == '__main__':
    serve()
