I have implemented changes for "P10-13 Advanced SIMD Tuning" in PR #40.
Please review the changes in `src/Pyrope.GarnetServer/Vector/VectorMath.cs` and `BruteForceVectorIndex.cs`.

**Changes:**
1.  Introduced `DotProductUnsafe` and `L2SquaredUnsafe` using `unsafe` blocks and `fixed` pointers.
2.  Implemented 4x Manual Loop Unrolling with 4 separate accumulators (`acc1`..`acc4`) to break dependency chains.
3.  Updated `BruteForceVectorIndex` to call these unsafe methods.

**Goal:**
Maximize throughput for vector distance calculations. Benchmarks show 1.77x improvement.

**Questions:**
1.  Are there any safety risks with the `fixed` pointer usage in `VectorMath` (e.g., GC pinning overhead)?
2.  Is there a better way to handle the tail loop?
3.  Any other micro-optimizations visible?

Please provide a code review summary.
