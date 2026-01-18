# Code Review: PR #42 (P10 Optimizations)

## Summary
The PR introduces significant performance improvements through better cache locality (List storage) and reduced allocations (ArrayPool and Spans). However, there are critical issues that must be addressed.

## Critical Issues

1.  **Bug in `Search` with `ArrayPool`**:
    *   **Issue**: `ArrayPool.Shared.Rent(Dimension)` returns an array of size >= Dimension.
    *   `VectorMath.L2Squared8Bit(byte[], byte[])` calls `ValidateInput` which throws if `a.Length != b.Length`.
    *   `_quantizedVectors[i]` has exact length `Dimension`. `qQuery` has length >= `Dimension`. This causes a mismatch.
    *   **Fix**: In `Search`, do not pass `qQuery` array directly. Use `VectorMath.L2Squared8Bit(qQuery.AsSpan(0, Dimension), ...)` to invoke the Span overload.

2.  **Stale Data in `Upsert`**:
    *   **Issue**: If `EnableQuantization` is false, `Upsert` updates the float vector but leaves `_quantizedVectors[index]` untouched (stale). If quantization is re-enabled later, it will use old data.
    *   **Fix**: In `Upsert`, if `!EnableQuantization`, explicitly set `_quantizedVectors[index] = Array.Empty<byte>()` (or match `InternalAdd` logic).

3.  **Memory Leak in `Delete`**:
    *   **Issue**: `Delete` sets `_isDeleted[index] = true` but keeps the `VectorEntry` and `byte[]` in the Lists. This prevents GC from reclaiming the memory of deleted large vectors.
    *   **Fix**: Set `_vectors[index] = null` (make `VectorEntry` class nullable or use a sentinel) and `_quantizedVectors[index] = null`.

## Minor Issues
*   **Thread Safety**: `EnableQuantization` should be volatile or accessed under lock, though impact is low.

## Recommendation
Fix the critical issues before merging.
