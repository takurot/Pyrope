I am planning to implement "Advanced SIMD Tuning" for my C# Vector Search engine (P10-13).
Based on previous advice, I plan to use **Unsafe Pointers** and **Loop Unrolling with Multiple Accumulators**.

**Plan:**
1.  Enable `AllowUnsafeBlocks` in `.csproj`.
2.  Create `DotProductUnsafe` and `L2SquaredUnsafe` in `VectorMath.cs`.
3.  Use `fixed (float* pA = a) fixed (float* pB = b)` to get pointers.
4.  Unroll the SIMD loop 4 times (consuming 4 `Vector<float>` per iteration).
5.  Use 4 separate accumulators (`acc1`, `acc2`, `acc3`, `acc4`) to increase Instruction Level Parallelism (ILP).

**Current Safe Implementation (Reference):**
```csharp
public static float DotProduct(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
{
    int count = Vector<float>.Count;
    var acc = Vector<float>.Zero;
    // ... single accumulator loop
}
```

**Request:**
Please review this plan and provide a **complete code example** for `DotProductUnsafe` and `L2SquaredUnsafe` implementing these optimizations.
- Ensure correct fallback handling for the tail (elements not divisible by 4 * VectorSize).
- Ensure it handles `Vector<float>` size dynamically (though usually 4 or 8 floats).

Also, are there any "gotchas" with pinning/GC I should be aware of in a high-throughput hot path?
