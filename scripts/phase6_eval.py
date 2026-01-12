import os
import time
import requests
import json
import random
import subprocess
import signal

# Config
BASE_URL = "http://127.0.0.1:5001"
ADMIN_KEY = "admin-key"
TENANT_ID = "bench_p6"
API_KEY = "apiKeyP6"
INDEX_NAME = "idx_p6"
DIM = 128

def log(msg):
    print(f"[Phase6 Eval] {msg}")

def request(method, path, body=None, admin=True):
    headers = {"X-API-KEY": ADMIN_KEY if admin else API_KEY}
    url = f"{BASE_URL}{path}"
    res = requests.request(method, url, json=body, headers=headers)
    if res.status_code >= 400:
        log(f"Request failed: {method} {path} -> {res.status_code} {res.text}")
    return res

def setup_env():
    log("Ensuring tenant exists...")
    request("POST", "/v1/tenants", {"tenantId": TENANT_ID, "apiKey": API_KEY, "quota": {"priority": 0}})
    
    log("Creating index...")
    request("POST", "/v1/indexes", {
        "tenantId": TENANT_ID,
        "indexName": INDEX_NAME,
        "dimension": DIM,
        "metric": "L2"
    })

def run_scenario_delta_indexing():
    log("=== Scenario 1: Delta Indexing ===")
    # Heavy UPSERT batch
    log("Running write-heavy benchmark...")
    # This will exercise Head (LSM)
    cmd = [
        "dotnet", "run", "--project", "src/Pyrope.Benchmarks", "--configuration", "Release", "--",
        "--host", "127.0.0.1", "--port", "3278",
        "--tenant", TENANT_ID, "--index", f"{INDEX_NAME}_delta", "--api-key", API_KEY,
        "--dataset", "synthetic",
        "--base-limit", "5000", "--query-limit", "100", "--dim", str(DIM), "--concurrency", "4"
    ]
    subprocess.run(cmd, check=True)
    log("Delta Indexing scenario complete.")

def run_scenario_semantic_caching():
    log("=== Scenario 2: Semantic Caching ===")
    # Generate a vector and push it as a centroid
    random.seed(1337)
    vector = [random.random() for _ in range(DIM)]
    
    # 1. UPSERT the vector
    log("Upserting test vector...")
    # We use VEC.UPSERT via Benchmark tool or just raw RESP?
    # For simplicity, use Benchmark tool to load 100 randoms
    cmd = [
        "dotnet", "run", "--project", "src/Pyrope.Benchmarks", "--configuration", "Release", "--",
        "--host", "127.0.0.1", "--port", "3278",
        "--tenant", TENANT_ID, "--index", INDEX_NAME, "--api-key", API_KEY,
        "--dataset", "synthetic",
        "--base-limit", "100", "--query-limit", "1", "--dim", str(DIM)
    ]
    subprocess.run(cmd, check=True)

    # 2. Push centroids
    log("Pushing centroids...")
    request("POST", f"/v1/indexes/{TENANT_ID}/{INDEX_NAME}/centroids", {
        "dimension": DIM,
        "centroids": [vector]
    })
    
    # 3. Run Benchmark with repeating queries (should HIT S-Cache L2)
    log("Running benchmark with 100% repetition...")
    cmd = [
        "dotnet", "run", "--project", "src/Pyrope.Benchmarks", "--configuration", "Release", "--",
        "--host", "127.0.0.1", "--port", "3278",
        "--tenant", TENANT_ID, "--index", INDEX_NAME, "--api-key", API_KEY,
        "--dataset", "synthetic",
        "--base-limit", "5000", "--query-limit", "500", "--dim", str(DIM), "--unique-queries", "1", "--repeat", "500",
        "--http", BASE_URL, "--admin-api-key", ADMIN_KEY, "--cache", "on"
    ]
    subprocess.run(cmd, check=True)
    log("Semantic Caching scenario complete.")

def run_scenario_prefetching():
    log("=== Scenario 3: Predictive Prefetching ===")
    # Needs two clusters
    random.seed(1337)
    v1 = [random.random() for _ in range(DIM)]
    random.seed(1338)
    v2 = [random.random() for _ in range(DIM)]
    
    log("Creating prefetch index...")
    request("POST", "/v1/indexes", {
        "tenantId": TENANT_ID,
        "indexName": f"{INDEX_NAME}_pf",
        "dimension": DIM,
        "metric": "L2"
    })

    log("Pushing two centroids (V1, V2)...")
    request("POST", f"/v1/indexes/{TENANT_ID}/{INDEX_NAME}_pf/centroids", {
        "dimension": DIM,
        "centroids": [v1, v2]
    })
    
    # Run sequence A -> B multiple times to train Sidecar
    log("Training prefetcher with sequence (V1 -> V2) x 5...")
    # We can't easily do "sequence" with current Benchmark tool without --sequence flag
    cmd = [
        "dotnet", "run", "--project", "src/Pyrope.Benchmarks", "--configuration", "Release", "--",
        "--host", "127.0.0.1", "--port", "3278",
        "--tenant", TENANT_ID, "--index", f"{INDEX_NAME}_pf", "--api-key", API_KEY,
        "--dataset", "synthetic",
        "--base-limit", "1000", "--query-limit", "10", "--dim", str(DIM), "--unique-queries", "2", "--sequence",
        "--http", BASE_URL, "--admin-api-key", ADMIN_KEY, "--cache", "off" # Don't cache during training
    ]
    subprocess.run(cmd, check=True)
    
    # Now enable cache and run A. B should be prefetched.
    # Wait a bit for sidecar to train/rules to sync
    time.sleep(2)
    
    log("Testing Prefetch: Running Query V1...")
    # First query (V1)
    cmd = [
        "dotnet", "run", "--project", "src/Pyrope.Benchmarks", "--configuration", "Release", "--",
        "--host", "127.0.0.1", "--port", "3278",
        "--tenant", TENANT_ID, "--index", f"{INDEX_NAME}_pf", "--api-key", API_KEY,
        "--dataset", "synthetic",
        "--query-limit", "1", "--dim", str(DIM), "--unique-queries", "1",
        "--http", BASE_URL, "--admin-api-key", ADMIN_KEY, "--cache", "on"
    ]
    subprocess.run(cmd, check=True)
    
    # Wait for prefetch to complete
    time.sleep(1)
    
    log("Checking if V2 is now in cache (should be hit)...")
    # Query V2 (should be a hit even if it's the first time we 'officially' search it in this run)
    # We use a custom seed or offset?
    # Benchmark tool uses Take(UniqueQueries). If unique=2, V1 is index 0, V2 is index 1.
    # If we run with unique=2 and query-limit=2, the first is V1, second is V2.
    
    cmd = [
        "dotnet", "run", "--project", "src/Pyrope.Benchmarks", "--configuration", "Release", "--",
        "--host", "127.0.0.1", "--port", "3278",
        "--tenant", TENANT_ID, "--index", f"{INDEX_NAME}_pf", "--api-key", API_KEY,
        "--dataset", "synthetic",
        "--query-limit", "2", "--dim", str(DIM), "--unique-queries", "2",
        "--http", BASE_URL, "--admin-api-key", ADMIN_KEY, "--cache", "on"
    ]
    subprocess.run(cmd, check=True)
    log("Predictive Prefetching scenario complete.")

if __name__ == "__main__":
    try:
        setup_env()
        run_scenario_delta_indexing()
        run_scenario_semantic_caching()
        run_scenario_prefetching()
    except Exception as e:
        log(f"Benchmark failed: {e}")
        exit(1)
