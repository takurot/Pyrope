import redis
import argparse
import sys
import json
import urllib.request
import urllib.error


def ensure_tenant(http_host, http_port, admin_api_key, tenant_id, api_key):
    url = f"http://{http_host}:{http_port}/v1/tenants"
    payload = json.dumps({"tenantId": tenant_id, "apiKey": api_key}).encode("utf-8")
    req = urllib.request.Request(
        url,
        data=payload,
        headers={"Content-Type": "application/json", "X-API-KEY": admin_api_key},
        method="POST",
    )
    try:
        with urllib.request.urlopen(req) as resp:
            print(f"Provisioned tenant '{tenant_id}': status {resp.status}")
    except urllib.error.HTTPError as e:
        if e.code == 409:
            print(f"Tenant '{tenant_id}' already exists.")
        else:
            print(f"Warning: Failed to provision tenant: {e.code} {e.reason}")
    except Exception as e:
        print(f"Warning: Could not connect to Control Plane HTTP API: {e}")


import os


def run_smoke_test(host, port, http_host="127.0.0.1", http_port=5000, admin_api_key=None, api_key=None):
    admin_api_key = admin_api_key or os.environ.get("PYROPE_ADMIN_API_KEY", "dev")
    api_key = api_key or os.environ.get("PYROPE_TENANT_API_KEY", "smoke_test_key")
    tenant_id = "smoke_tenant"
    index_name = "test_index"

    # 0. Provision tenant if HTTP API available
    if http_port and admin_api_key:
        print(f"Provisioning tenant at http://{http_host}:{http_port}...")
        ensure_tenant(http_host, http_port, admin_api_key, tenant_id, api_key)

    print(f"Connecting to Garnet at {host}:{port} with credentials...")
    try:
        r = redis.Redis(
            host=host,
            port=port,
            password=f"{tenant_id}:{api_key}",
            decode_responses=True,
        )
        r.ping()
        print("Connected and authenticated.")
    except redis.exceptions.ConnectionError as e:
        print(f"Failed to connect: {e}")
        return False
    except Exception as e:
        print(f"Connection/Auth error: {e}")
        return False

    # 1. Clean up potential leftovers
    print("Cleaning up previous run...")
    try:
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
        res1 = r.execute_command(
            "VEC.ADD",
            tenant_id,
            index_name,
            "doc1",
            "VECTOR",
            json.dumps(vec1),
            "META",
            json.dumps({"type": "test", "id": 1}),
        )
        if res1 != "VEC_OK":
            print(f"VEC.ADD doc1 failed: {res1}")
            return False

        res2 = r.execute_command(
            "VEC.ADD",
            tenant_id,
            index_name,
            "doc2",
            "VECTOR",
            json.dumps(vec2),
            "TAGS",
            json.dumps(["tagA"]),
        )
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
        res = r.execute_command(
            "VEC.SEARCH",
            tenant_id,
            index_name,
            "TOPK",
            "2",
            "VECTOR",
            json.dumps([0.9, 0.1, 0.0]),
            "WITH_META",
        )

        if not isinstance(res, list):
            print(f"VEC.SEARCH returned unexpected type: {type(res)}")
            return False

        if len(res) == 0:
            print("VEC.SEARCH returned no results")
            return False

        first_hit = res[0]
        if first_hit[0] != "doc1":
            print(f"VEC.SEARCH unexpected top 1: {first_hit[0]}, expected doc1")
            return False

        # Search for vec2 with filter
        res_filter = r.execute_command(
            "VEC.SEARCH",
            tenant_id,
            index_name,
            "TOPK",
            "1",
            "VECTOR",
            json.dumps([0.0, 0.9, 0.0]),
            "FILTER",
            json.dumps(["tagA"]),
        )
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
        res = r.execute_command("VEC.DEL", tenant_id, index_name, "doc1")
        if res != "VEC_OK":
            print(f"VEC.DEL failed: {res}")
            return False

        # Verify deletion
        res_search = r.execute_command(
            "VEC.SEARCH", tenant_id, index_name, "TOPK", "1", "VECTOR", json.dumps([1.0, 0.0, 0.0])
        )
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
    parser = argparse.ArgumentParser(description="Pyrope Smoke Test")
    parser.add_argument("--host", default="127.0.0.1", help="Garnet host")
    parser.add_argument("--port", type=int, default=6379, help="Garnet port")
    parser.add_argument("--http-host", default="127.0.0.1", help="Control plane HTTP host")
    parser.add_argument("--http-port", type=int, default=5000, help="Control plane HTTP port")
    parser.add_argument("--admin-api-key", default=None, help="Admin API key")
    parser.add_argument("--api-key", default=None, help="Tenant API key")
    args = parser.parse_args()

    success = run_smoke_test(
        args.host,
        args.port,
        http_host=args.http_host,
        http_port=args.http_port,
        admin_api_key=args.admin_api_key,
        api_key=args.api_key,
    )
    if success:
        print("\nSmoke Test PASSED")
        sys.exit(0)
    else:
        print("\nSmoke Test FAILED")
        sys.exit(1)
