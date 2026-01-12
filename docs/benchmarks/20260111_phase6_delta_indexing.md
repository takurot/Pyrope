# Benchmark Results: Phase 6-1 Delta Indexing

**Date:** 2026-01-11
**Version:** Phase 6-1 (DeltaVectorIndex implementation)
**Configuration:**
- **Index Type:** DeltaVectorIndex (Head=BruteForce, Tail=BruteForce)
- **Compaction:** Not running (Tail is empty, all data in Head)
- **Dataset:** Synthetic (Dimension 32)
- **Vectors:** 5,000
- **Queries:** 1,000
- **Concurrency:** 4

## Results

```
[Pyrope.Benchmarks] RESP endpoint: 127.0.0.1:6379
[Pyrope.Benchmarks] tenant=tenant_bench index=idx_bench
[Pyrope.Benchmarks] dataset=synthetic payload=binary(float32)
[Pyrope.Benchmarks] Loading base vectors: 5000 ...
[Pyrope.Benchmarks] Loading: 5000/5000 (100.0%) - 16724.6 vec/s
[Pyrope.Benchmarks] Loaded=5000 elapsed=2.46s throughput=2031.6 vec/s

[Pyrope.Benchmarks] Warmup: 100 queries ...

[Pyrope.Benchmarks] Benchmark: queries=1000 topK=10 concurrency=4 ...
[Pyrope.Benchmarks] Searching: 57/1000 (5.7%) - 55.2 QPS
...
[Pyrope.Benchmarks] Latency(ms): min=1.47 p50=20.078 p95=209.121 p99=707.72 max=1310.469 mean=48.221
```

## Observations
- **Functionality**: Validated end-to-end `VEC.UPSERT` and `VEC.SEARCH` through `DeltaVectorIndex`.
- **Latency**: P99 is high (~700ms), potentially due to JIT compilation during the short run or unoptimized double-checking of (empty) Tail index + Head index.
- **Throughput**: Load throughput is healthy (~2k/s). Search QPS (~55) is low for synthetic data, likely due to small dataset overhead/concurrency settings in this specific run.
