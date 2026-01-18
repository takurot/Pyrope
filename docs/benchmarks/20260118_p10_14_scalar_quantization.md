# Benchmark Result: Scalar Quantization (P10-14)

**Date**: 2026-01-18
**Task**: P10-14 Scalar Quantization (SQ8)
**Environment**: Local (Mac, .NET 8.0)

## Summary

Implemented `ScalarQuantizer` (Float -> Byte) and SIMD-accelerated `L2Squared8Bit` using `unsafe` pointers and `Vector.Widen`.
Benchmarked on Synthetic 128-dim dataset (100k vectors).

## Results

| Metric | Baseline (Float32) | SQ8 (Optimized) | Speedup |
| :--- | :--- | :--- | :--- |
| **Throughput (QPS)** | 298.8 QPS | **461.4 QPS** | **1.54x** |
| **Mean Latency** | 13.4 ms | **8.7 ms** | - |
| **P50 Latency** | 4.2 ms | 8.1 ms | - |

**Observation**: SQ8 provides a **54% throughput increase** over optimized Float32.
Lower memory bandwidth usage (12MB vs 50MB) and efficient SIMD instructions contribute to the speedup.
P50 latency for Float32 was lower (4ms) but had high tail latency (P99 144ms), whereas SQ8 was more consistent (P99 20ms).
This suggests SQ8 reduces GC pressure significantly (less memory churn or better cache locality).

## Implementation Details
- **Quantization**: Min-Max scaling per vector (stored as `byte[]` and `(min, max)` tuple).
- **Distance**: `L2Squared8Bit` uses `fixed` pointers, 4x loop unrolling, and `Vector.Widen` chain.
- **Index**: `BruteForceVectorIndex` updated to support `EnableQuantization` flag.

