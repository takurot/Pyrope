# Code Review: P10-14 Scalar Quantization

## Summary
The Scalar Quantization (SQ8) implementation provides a good foundation but has several issues that need addressing before production.

## Critical Issues

1.  **SIMD Bug in `DotProduct8Bit`**:
    *   **Location**: `src/Pyrope.GarnetServer/Vector/VectorMath.cs`
    *   **Issue**: The loop increments by `vectorCount` (1 block) but the `end` condition uses `stride` (4 * `vectorCount`). This causes the loop to terminate much earlier than intended, leaving most of the work to the scalar tail, negating SIMD benefits.
    *   **Fix**: Update loop increment to `stride` or adjust `end` calculation.

2.  **Hardcoded Logic**:
    *   **Location**: `src/Pyrope.GarnetServer/Vector/BruteForceVectorIndex.cs` in `Add` method.
    *   **Issue**: `if (EnableQuantization || true)` forces quantization regardless of the flag. `Upsert` also lacks the check.
    *   **Fix**: Remove `|| true` and ensure both `Add` and `Upsert` respect `EnableQuantization`.

## Major Findings

3.  **Accuracy/Methodology**:
    *   **Issue**: The implementation uses **Local Scalar Quantization** (min/max per vector) but the search method uses `DotProduct8Bit` which compares raw bytes directly. This ignores the scaling factors ($min$ and $range$), leading to incorrect rankings if vectors have significantly different dynamic ranges.
    *   **Recommendation**: For now, acknowledge this as an approximation. Future work should implement correct LSQ scoring or switch to Global Scalar Quantization (requires dataset calibration).

4.  **Performance**:
    *   **Issue**: `BruteForceVectorIndex.Search` iterates over a `Dictionary<string, byte[]>`. Dictionary iteration is significantly slower than array iteration due to non-contiguous memory and overhead.
    *   **Recommendation**: Future optimization (P10-12) should move to a contiguous memory layout (e.g., `List<byte[]>` or a single large `byte[]` buffer).

## Conclusion
The implementation works functionally but requires the critical fixes above. The accuracy limitation is acceptable for this prototype phase but must be documented.
