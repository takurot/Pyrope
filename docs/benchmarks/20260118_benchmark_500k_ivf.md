# Benchmark Results: Synthetic Dataset (500,000 Vectors) - IVF-Flat Index

**Date**: 2026-01-18
**Environment**: Local Development (macOS/Arm64)
**Mode**: Release
**Version**: P10 Optimization Suite + IVF-Flat Index

## Configuration
- **Dataset**: Synthetic (Random Float32)
- **Dimension**: 128
- **Base Vectors**: 500,000
- **Queries**: 100
- **TopK**: 10
- **Concurrency**: 4
- **Index Type**: IVF-Flat (via `--build-index`)

## Results

### Throughput (Load)
- **Rate**: 20,826.1 vec/s
- **Total Time**: 24.01s

### Search Performance
- **QPS**: 192.7
- **Total Time**: 0.52s

### Latency Distribution
| Metric | Value |
| :--- | :--- |
| **Min** | 5.500 ms |
| **Mean** | 20.587 ms |
| **P50** | 18.755 ms |
| **P95** | 30.243 ms |
| **P99** | 32.001 ms |
| **Max** | 32.351 ms |

## Scalability Summary (IVF-Flat)

| Vectors | QPS | P99 Latency | Load Time | Status |
| ---: | ---: | ---: | ---: | :--- |
| 100k | 673.2 | 9.75 ms | 4.35s | ✅ Excellent |
| 500k | 192.7 | 32.00 ms | 24.01s | ✅ Within SLA |
| 1M | - | - | - | ❌ Stalled at 93% |

## Analysis

### Key Findings

1.  **500k Successfully Completes**: Unlike the 1M attempt that stalled, 500k vectors load and index successfully.

2.  **P99 Still Within SLA**: At 32ms P99, the system remains **within typical RAG SLA requirements** (50ms target).

3.  **Linear QPS Degradation**: QPS dropped from 673 (100k) to 193 (500k), roughly proportional to the 5x data increase. This is expected for IVF-Flat with constant `nprobe`.

4.  **1M Limit Identified**: The system appears to have a memory or stability issue between 500k and 1M vectors. Further investigation needed (likely GC pressure or Garnet memory limits).

### Recommended Next Steps

1.  **Investigate 1M Stall**: Check server memory consumption, GC logs, and Garnet internals to identify the bottleneck.
2.  **Tune IVF Parameters**: Increase `nlist` for 1M+ scale to reduce per-cluster size and improve pruning.
3.  **Consider IVF-PQ**: For 1M+ vectors, Product Quantization would reduce memory by ~4-8x.

### Conclusion

**500k vectors is the current practical limit for the local development environment.** Production deployments with more memory should be able to handle 1M+, but this requires further investigation and tuning.
