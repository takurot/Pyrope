#!/bin/bash
set -e

# Configuration
BASE_URL="http://localhost:5000/v1/ai"
ADMIN_KEY="admin-secret-key"

echo "=== Testing AI Model Lifecycle ==="

# 1. List Models (Initial)
echo "[1] Listing models..."
curl -s -H "X-API-KEY: $ADMIN_KEY" "$BASE_URL/models" | jq .

# 2. Train Model
echo "\n[2] Triggering training..."
JOB_ID=$(curl -s -X POST -H "X-API-KEY: $ADMIN_KEY" -H "Content-Type: application/json" -d '{"dataset_path": "logs/query_log.jsonl"}' "$BASE_URL/models/train" | jq -r .job_id)
echo "Training Job ID: $JOB_ID"

if [ "$JOB_ID" == "null" ]; then
    echo "Training failed to start."
    exit 1
fi

# Wait for training to complete (mocking delay)
echo "Waiting for training..."
sleep 5

# 3. Verify New Model Listed
echo "\n[3] Verifying new model..."
LATEST_VERSION=$(curl -s -H "X-API-KEY: $ADMIN_KEY" "$BASE_URL/models" | jq -r '.models[0].version')
echo "Latest Version: $LATEST_VERSION"

if [ "$LATEST_VERSION" == "null" ]; then
    echo "No new model found."
    exit 1
fi

# 4. Deploy Canary
echo "\n[4] Deploying as Canary for tenant 'tenant_beta'..."
curl -s -X POST -H "X-API-KEY: $ADMIN_KEY" -H "Content-Type: application/json" \
    -d "{\"version\": \"$LATEST_VERSION\", \"canary\": true, \"canary_tenants\": [\"tenant_beta\"]}" \
    "$BASE_URL/models/deploy" | jq .

# Verify Canary State
CANARY_VERSION=$(curl -s -H "X-API-KEY: $ADMIN_KEY" "$BASE_URL/models" | jq -r .canary_model_version)
if [ "$CANARY_VERSION" != "$LATEST_VERSION" ]; then
    echo "Canary deployment failed. Expected $LATEST_VERSION, got $CANARY_VERSION"
    exit 1
fi

# 5. Rollback Canary
echo "\n[5] Rolling back Canary..."
curl -s -X POST -H "X-API-KEY: $ADMIN_KEY" -H "Content-Type: application/json" \
    -d '{"canary_only": true}' \
    "$BASE_URL/models/rollback" | jq .

# Verify Rollback
CANARY_VERSION=$(curl -s -H "X-API-KEY: $ADMIN_KEY" "$BASE_URL/models" | jq -r .canary_model_version)
if [ "$CANARY_VERSION" != "none" ]; then
    echo "Rollback failed. Expected none, got $CANARY_VERSION"
    exit 1
fi

echo "\n=== AI Lifecycle Test Passed ==="
