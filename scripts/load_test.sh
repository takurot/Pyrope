#!/bin/bash
# Load Test Script for Pyrope Vector DB (P5-8)
# Tests SLO compliance under various load conditions

set -e

# Default Configuration
HOST="${HOST:-127.0.0.1}"
PORT="${PORT:-3278}"
HTTP="${HTTP:-http://127.0.0.1:5000}"
TENANT="${TENANT:-test_load}"
INDEX="${INDEX:-load_idx}"
API_KEY="${API_KEY:-test-api-key}"
ADMIN_API_KEY="${ADMIN_API_KEY:-admin-key}"
DIM="${DIM:-128}"
BASE_LIMIT="${BASE_LIMIT:-10000}"
QUERY_LIMIT="${QUERY_LIMIT:-5000}"
TOPK="${TOPK:-10}"
WARMUP="${WARMUP:-500}"

# SLO Targets
SLO_P99_MS="${SLO_P99_MS:-50}"  # P99 latency target in ms

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo "============================================="
echo "Pyrope Load Test Suite"
echo "============================================="
echo "Host: $HOST:$PORT"
echo "HTTP: $HTTP"
echo "Tenant: $TENANT"
echo "Index: $INDEX"
echo "Dimension: $DIM"
echo "Vectors: $BASE_LIMIT"
echo "Queries: $QUERY_LIMIT"
echo "SLO P99: ${SLO_P99_MS}ms"
echo "============================================="

# Function to run benchmark with specific concurrency
run_benchmark() {
    local concurrency=$1
    local cache_mode=$2
    
    echo ""
    echo -e "${YELLOW}--- Testing: Concurrency=$concurrency, Cache=$cache_mode ---${NC}"
    
    local log_file="/tmp/loadtest_c${concurrency}_${cache_mode}.log"
    dotnet run --project src/Pyrope.Benchmarks --configuration Release -- \
        --dataset synthetic \
        --dim "$DIM" \
        --base-limit "$BASE_LIMIT" \
        --query-limit "$QUERY_LIMIT" \
        --topk "$TOPK" \
        --concurrency "$concurrency" \
        --warmup "$WARMUP" \
        --payload binary \
        --host "$HOST" \
        --port "$PORT" \
        --tenant "$TENANT" \
        --index "$INDEX" \
        --api-key "$API_KEY" \
        --http "$HTTP" \
        --admin-api-key "$ADMIN_API_KEY" \
        --cache "$cache_mode" \
        --print-stats 2>&1 | tee "$log_file"
    
    # Extract values using awk (more portable than grep -P)
    local qps=$(awk -F'QPS=' '/QPS=/{print $2}' "$log_file" | head -n1)
    local p99=$(awk -F'p99=' '/p99=/{print $2}' "$log_file" | awk '{print $1}' | head -n1)
    
    if [[ -z "$qps" ]]; then qps="N/A"; fi
    if [[ -z "$p99" ]]; then p99="N/A"; fi
    
    echo ""
    echo "Results: P99=${p99}ms, QPS=${qps}"
    
    # Check SLO compliance
    if [[ "$p99" != "N/A" ]]; then
        # Use awk for float comparison (works on macOS without bc)
        local passed=$(awk -v p99="$p99" -v slo="$SLO_P99_MS" 'BEGIN { if (p99 <= slo) print "1"; else print "0" }')
        if [[ "$passed" == "1" ]]; then
            echo -e "${GREEN}✓ SLO PASS: P99 ($p99 ms) <= Target ($SLO_P99_MS ms)${NC}"
        else
            echo -e "${RED}✗ SLO VIOLATION: P99 ($p99 ms) > Target ($SLO_P99_MS ms)${NC}"
            return 1
        fi
    else
        echo -e "${RED}✗ Failed to parse benchmark results.${NC}"
        return 1
    fi
    
    return 0
}

# Track overall status
OVERALL_STATUS=0

echo ""
echo "============================================="
echo "Phase 1: Low Load Testing (Cache Off)"
echo "============================================="

for conc in 1 4; do
    if ! run_benchmark $conc "off"; then
        OVERALL_STATUS=1
    fi
done

echo ""
echo "============================================="
echo "Phase 2: Medium Load Testing (Cache On)"
echo "============================================="

for conc in 8 16 32; do
    if ! run_benchmark $conc "on"; then
        OVERALL_STATUS=1
    fi
done

echo ""
echo "============================================="
echo "Phase 3: High Load Stress Testing"
echo "============================================="

for conc in 50 100; do
    if ! run_benchmark $conc "on"; then
        OVERALL_STATUS=1
    fi
done

echo ""
echo "============================================="
echo "Load Test Complete"
echo "============================================="

if [[ $OVERALL_STATUS -eq 0 ]]; then
    echo -e "${GREEN}All tests passed SLO requirements!${NC}"
else
    echo -e "${RED}Some tests failed SLO requirements.${NC}"
    echo "Review logs in /tmp/loadtest_*.log for details."
fi

exit $OVERALL_STATUS
