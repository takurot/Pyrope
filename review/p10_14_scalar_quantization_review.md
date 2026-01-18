# Code Review Request: P10-14 Scalar Quantization

## Summary
Implemented P10-14: Scalar Quantization (SQ8) for vector search.
This introduces `ScalarQuantizer` for 8-bit quantization and SIMD-optimized distance calculations (`L2Squared8Bit`, `DotProduct8Bit`) using `System.Runtime.Intrinsics`.

## Changes
1.  **New `ScalarQuantizer` class**: Handles `float[]` -> `byte[]` quantization (linear scaling) and dequantization.
2.  **Updated `VectorMath`**: Added `L2Squared8Bit` and `DotProduct8Bit` with unsafe/fixed pointers and SIMD optimization (AVX2/SSE implicit via `Vector<T>`).
3.  **Updated `BruteForceVectorIndex`**:
    *   Added `EnableQuantization` property.
    *   Maintains `_quantizedVectors` (byte[]) alongside standard `_entries`.
    *   `Search` method now has a dedicated path for quantized search when enabled.
4.  **Tests**: Added `ScalarQuantizerTests`.

## Benchmarks
Preliminary benchmarks show ~1.54x throughput improvement on 100k vectors with BruteForce index.

## Questions for Reviewer
*   Is the SIMD implementation in `VectorMath.cs` optimal? Specifically `DotProduct8Bit`.
*   Is the integration in `BruteForceVectorIndex` thread-safe regarding the new `_quantizedVectors` dictionary? (It is protected by the same lock, but double check).
*   Should we expose `EnableQuantization` in `IndexConfig` or `IndexMetadata` now, or wait for a future task? (Currently defaults to false).

## Request
Please review the implementation for correctness, performance, and code style.
