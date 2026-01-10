#!/bin/bash
set -euo pipefail

# Wrapper for the P1-6 vector benchmarking tool.
# Usage:
#   ./scripts/bench_vectors.sh --dataset sift --sift-dir ./datasets/sift1m --base-limit 100000 --query-limit 1000

cd "$(dirname "$0")/.."
dotnet run --project src/Pyrope.Benchmarks -- "$@"

