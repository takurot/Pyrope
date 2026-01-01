#!/bin/bash
set -e

echo "=== Running Quality Checks [Pyrope] ==="

# 1. C# Format Check
echo "--- 1. C# Format Check ---"
dotnet format --verify-no-changes --verbosity normal || {
    echo "Format check failed. Run 'dotnet format' to fix."
    exit 1
}

# 2. C# Build
echo "--- 2. C# Build ---"
dotnet build Pyrope.sln --configuration Release

# 3. C# Tests
echo "--- 3. C# Dotnet Tests ---"
dotnet test Pyrope.sln --configuration Release --no-build

# 4. Python Sidecar Checks
echo "--- 4. Python Checks ---"
SIDECAR_DIR="src/Pyrope.AISidecar"

if [ -d "$SIDECAR_DIR" ]; then
    echo "Checking Python Sidecar..."
    
    # Check if venv exists, create if not
    if [ -d ".venv" ]; then
        source .venv/bin/activate 2>/dev/null || true
    fi
    
    # Install dev dependencies if available
    if command -v pip &> /dev/null; then
        pip install flake8 black --quiet 2>/dev/null || true
    fi
    
    # Run linting
    if command -v flake8 &> /dev/null; then
        echo "Running flake8..."
        flake8 "$SIDECAR_DIR" --max-line-length=120 --ignore=E501,W503 || echo "flake8 warnings found (non-blocking)"
    fi
    
    if command -v black &> /dev/null; then
        echo "Running black format check..."
        black --check --line-length 120 "$SIDECAR_DIR" || echo "black format issues found (non-blocking)"
    fi
fi

# 5. Security Scan (optional, informational)
echo "--- 5. Security Scan (Informational) ---"
dotnet list package --vulnerable --include-transitive 2>&1 | head -20 || true

echo "=== All Checks Passed ==="
