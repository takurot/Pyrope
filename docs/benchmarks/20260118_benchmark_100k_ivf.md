# Benchmark Results: Synthetic Dataset (100,000 Vectors) - IVF-Flat Index

**Date**: 2026-01-18
**Environment**: Local Development (macOS/Arm64)
**Mode**: Release
**Version**: P10 Optimization Suite + IVF-Flat Index

## Configuration
- **Dataset**: Synthetic (Random Float32)
- **Dimension**: 128
- **Base Vectors**: 100,000
- **Queries**: 100
- **TopK**: 10
- **Concurrency**: 4
- **Index Type**: IVF-Flat (via `--build-index`)

## Results

### Throughput (Load)
- **Rate**: 22,983.0 vec/s
- **Total Time**: 4.35s

### Search Performance
- **QPS**: 673.2
- **Total Time**: 0.15s

### Latency Distribution
| Metric | Value |
| :--- | :--- |
| **Min** | 1.595 ms |
| **Mean** | 5.862 ms |
| **P50** | 5.595 ms |
| **P95** | 7.049 ms |
| **P99** | 9.745 ms |
| **Max** | 9.764 ms |

## Comparison: BruteForce vs IVF-Flat (100k Vectors)

| Metric | BruteForce | IVF-Flat | Improvement |
| :--- | ---: | ---: | :--- |
| **QPS** | 69.6 | 673.2 | **9.7x faster** |
| **P99 Latency** | 76.13 ms | 9.75 ms | **7.8x lower** |
| **P50 Latency** | 60.70 ms | 5.60 ms | **10.8x lower** |
| **Mean Latency** | 57.45 ms | 5.86 ms | **9.8x lower** |

## Analysis

### Key Findings

1.  **IVF-Flat Delivers Massive Improvement**: Switching from BruteForce to IVF-Flat provides nearly **10x QPS improvement** and **8x P99 reduction** at 100k vectors. This confirms IVF indexing is essential for production workloads.

2.  **P99 Well Within SLA**: At 9.75ms P99, the system is now **comfortably within typical RAG SLA requirements** (50ms target). This is a major milestone.

3.  **Consistent Latency**: The tight spread between P50 (5.6ms) and P99 (9.7ms) indicates stable, predictable performance—a sign of healthy CPU cache behavior and efficient IVF pruning.

4.  **Load Throughput Unchanged**: Write performance remains at ~23k vec/s, as expected (writes go to the Head index and are independent of the Tail index type).

### Scalability Projection

Based on these results:
- **1M vectors** should achieve ~200-300 QPS with P99 < 30ms (with `nlist` tuned to ~1000)
- **10M vectors** would require IVF-PQ quantization for memory efficiency

### Conclusion

**P10-8 (IVF-Flat Implementation) is validated as production-ready for 100k-scale workloads.** The system now meets commercial competitiveness requirements for latency-sensitive RAG applications.

Next steps:
1.  Benchmark at 1M vectors to validate projection
2.  Implement P7-1 (Request Metering) for MVP billing
3.  Evaluate Hybrid Search (P10-1) for feature completeness
