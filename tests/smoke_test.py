import redis
import argparse
import sys
import json
import time

def run_smoke_test(host, port):
    print(f"Connecting to Garnet at {host}:{port}...")
    try:
        r = redis.Redis(host=host, port=port, decode_responses=True)
        r.ping()
        print("Connected.")
    except redis.exceptions.ConnectionError as e:
        print(f"Failed to connect: {e}")
        return False

    tenant_id = "smoke_tenant"
    index_name = "test_index"
    
    # 1. Clean up potential leftovers
    print("Cleaning up previous run...")
    try:
        # We don't have a DROP INDEX yet, so we just del known keys if any
        # Or just proceed.
        r.execute_command("VEC.DEL", tenant_id, index_name, "doc1")
        r.execute_command("VEC.DEL", tenant_id, index_name, "doc2")
    except Exception:
        pass

    # 2. VEC.ADD
    print("Testing VEC.ADD...")
    vec1 = [1.0, 0.0, 0.0]
    vec2 = [0.0, 1.0, 0.0]
    
    try:
        # VEC.ADD tenant index id VECTOR <json> [META <json>] [TAGS <json>]
        res1 = r.execute_command("VEC.ADD", tenant_id, index_name, "doc1", "VECTOR", json.dumps(vec1), "META", json.dumps({"type": "test", "id": 1}))
        if res1 != "VEC_OK":
            print(f"VEC.ADD doc1 failed: {res1}")
            return False
            
        res2 = r.execute_command("VEC.ADD", tenant_id, index_name, "doc2", "VECTOR", json.dumps(vec2), "TAGS", json.dumps(["tagA"]))
        if res2 != "VEC_OK":
            print(f"VEC.ADD doc2 failed: {res2}")
            return False
        print("VEC.ADD Passed.")
    except Exception as e:
        print(f"VEC.ADD Exception: {e}")
        return False

    # 3. VEC.SEARCH
    print("Testing VEC.SEARCH...")
    try:
        # VEC.SEARCH tenant index TOPK k VECTOR <json> [FILTER <tags>] [WITH_META]
        # Search for vec1, should retrieve doc1 first
        res = r.execute_command("VEC.SEARCH", tenant_id, index_name, "TOPK", "2", "VECTOR", json.dumps([0.9, 0.1, 0.0]), "WITH_META")
        
        # Expecting a list of results
        if not isinstance(res, list):
            print(f"VEC.SEARCH returned unexpected type: {type(res)}")
            return False
        
        if len(res) == 0:
            print("VEC.SEARCH returned no results")
            return False
            
        # Parse result: [ [id, score, meta], [id, score, meta] ]
        # Note: Depending on implementation, it might be flat or nested. 
        # Based on CommandRegistryTests.cs: output.WriteArrayLength(results.Count); then 3 elements per hit.
        # So redis-py might parse this as a list of lists if RESP2/3 arrays are used correctly.
        # Garnet typically uses RESP2 by default, so List of List is expected if WriteArrayLength usage matches.
        
        first_hit = res[0]
        if first_hit[0] != "doc1":
             print(f"VEC.SEARCH unexpected top 1: {first_hit[0]}, expected doc1")
             return False
             
        # Search for vec2 with filter
        res_filter = r.execute_command("VEC.SEARCH", tenant_id, index_name, "TOPK", "1", "VECTOR", json.dumps([0.0, 0.9, 0.0]), "FILTER", json.dumps(["tagA"]))
        if len(res_filter) == 0:
             print("VEC.SEARCH with FILTER returned no results")
             return False
             
        if res_filter[0][0] != "doc2":
             print(f"VEC.SEARCH with FILTER unexpected top 1: {res_filter[0][0]}, expected doc2")
             return False

        print("VEC.SEARCH Passed.")
    except Exception as e:
        print(f"VEC.SEARCH Exception: {e}")
        return False

    # 4. VEC.DEL
    print("Testing VEC.DEL...")
    try:
        # Usage: VEC.DEL <tenantId> <indexName> <id>
        res = r.execute_command("VEC.DEL", tenant_id, index_name, "doc1")
        if res != "VEC_OK":
            print(f"VEC.DEL failed: {res}")
            return False
            
        # Verify deletion
        res_search = r.execute_command("VEC.SEARCH", tenant_id, index_name, "TOPK", "1", "VECTOR", json.dumps([1.0, 0.0, 0.0]))
        # Should not find doc1. Might find doc2 or nothing.
        for hit in res_search:
            if hit[0] == "doc1":
                print("VEC.DEL failed: doc1 still found in search")
                return False

        print("VEC.DEL Passed.")

    except Exception as e:
        print(f"VEC.DEL Exception: {e}")
        return False
        
    return True

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description='Pyrope Smoke Test')
    parser.add_argument('--host', default='127.0.0.1', help='Garnet host')
    parser.add_argument('--port', type=int, default=6379, help='Garnet port')
    args = parser.parse_args()

    success = run_smoke_test(args.host, args.port)
    if success:
        print("\nSmoke Test PASSED")
        sys.exit(0)
    else:
        print("\nSmoke Test FAILED")
        sys.exit(1)
