I am implementing **Task P10-14: Scalar Quantization (SQ8)** for my Vector Search engine in C# (.NET 8).
I need to implement high-performance SIMD distance calculations for 8-bit quantized vectors (`byte[]`).

**Goal:**
1.  **Quantize**: `float[]` (32-bit) -> `byte[]` (8-bit) using Min/Max scaler.
2.  **Distance**:
    *   `L2Squared8Bit(byte[] a, byte[] b)`
    *   `DotProduct8Bit(byte[] a, byte[] b)`

**Constraints:**
-   Use `System.Numerics.Vector<T>` or `System.Runtime.Intrinsics` (AVX2/NEON compatible).
-   Avoid intermediate conversions to `float` as much as possible for performance.
-   Be aware of overflow (sum of squared byte differences can exceed `byte` and `short`).
-   Target Hardware: Apple Silicon (NEON) and x64 (AVX2).

**Request:**
Please provide a **complete, highly optimized code example** for:
1.  `ScalarQuantizer` class (Quantize/Dequantize).
2.  `VectorMath.L2Squared8Bit` using SIMD.
    -   *Tip:* Should I use `Vector.Widen`? Or `Vector<short>`?
    -   *Optimization:* Loop unrolling?
3.  `VectorMath.DotProduct8Bit`.

Also, how accurate is SQ8 L2 usually compared to Float32?
