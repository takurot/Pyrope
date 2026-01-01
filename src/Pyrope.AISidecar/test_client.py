import grpc
import policy_service_pb2
import policy_service_pb2_grpc


def run():
    print("Connecting to server...")
    with grpc.insecure_channel('localhost:50051') as channel:
        stub = policy_service_pb2_grpc.PolicyServiceStub(channel)
        print("Sending request...")
        response = stub.GetIndexPolicy(policy_service_pb2.IndexPolicyRequest(
            tenant_id="test_tenant",
            index_name="test_index"
        ))
        print("Received response:")
        print(f"  PQ M: {response.pq_m}")
        print(f"  PQ Cons: {response.pq_construction}")
        print(f"  PCA Dim: {response.pca_dimension}")
        print(f"  Status: {response.status}")


if __name__ == '__main__':
    run()
