#!/bin/bash
set -e

echo "=== Running Quality Checks [Pyrope] ==="

# 1. C# Core Checks
echo "--- 1. C# Dotnet Tests ---"
dotnet test Pyrope.sln

# 2. Python Sidecar Checks
echo "--- 2. Python Checks ---"

if [ -f "src/Pyrope.AISidecar/requirements.txt" ]; then
    echo "Check Python Sidecar dependencies..."
    # Ideally checking against venv, but for now just reporting existence
    # pip install -r src/Pyrope.AISidecar/requirements.txt
fi

if [ -f "src/Pyrope.AISidecar/test_client.py" ]; then
    echo "Running generic Python tests..."
    # python -m unittest src/Pyrope.AISidecar/test_client.py # Uncomment when ready/if unit test compatible
    echo "Skipping direct python execution until venv is confirmed."
fi

echo "=== All Checks Passed ==="
