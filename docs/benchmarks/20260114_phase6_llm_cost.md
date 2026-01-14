# Phase 6 Cost-Aware & LLM Integration Benchmarks (2026-01-14)

## Overview
This report tracks benchmark results for Phase 6 features P6-6 through P6-12.

## Benchmark History

| Date | Phase | Configuration | Write (vec/s) | Search QPS | P50 (ms) | P99 (ms) | Notes |
|------|-------|---------------|---------------|------------|----------|----------|-------|
| 2026-01-14 | P6-6/7 (Standalone) | 10k vec, 128d, 4 conc | 16,268 | 167.3 | 22.4 | 42.2 | Baseline with Cost-Aware + Semantic TTL |
| 2026-01-14 | P6-8 (w/ Sidecar) | 10k vec, 128d, 4 conc | 10,615 | 123.2 | 27.6 | 128.4 | LLM Worker integrated, gRPC overhead |
| 2026-01-14 | P6-9~12 (Standalone) | 10k vec, 128d, 4 conc | 15,252 | 144.2 | 24.8 | 93.8 | Full Phase 6 features |

## Key Observations

### Cost-Aware Query Routing (P6-6)
- Budget tracking adds negligible overhead (~0.1ms)
- Degradation triggers correctly when `MonthlyBudget` exceeded
- `MaxScans` reduced by 50% on over-budget tenants

### Semantic TTL (P6-7)
- Cluster heat detection working (10 writes/min threshold)
- Hot clusters get 10% of base TTL (minimum 1s)
- O(K) cluster lookup per write (~256 clusters = ~0.1ms)

### LLM Integration (P6-8 ~ P6-12)
- Sidecar adds ~30% latency overhead due to gRPC keepalive
- Rate limiting: 60 req/min, 100k tokens/min
- Monthly budget tracking for cost control

## Previous Benchmarks

| Date | Feature | Config | Write | QPS | P99 | Reference |
|------|---------|--------|-------|-----|-----|-----------|
| 2026-01-12 | Delta Indexing | 5k vec | 1,165 | 1,861 | 5.7ms | [S1](20260112_phase6_summary.md) |
| 2026-01-12 | Semantic Cache | Repeated query | - | 6,122 | 14.5ms | [S2](20260112_phase6_summary.md) |
| 2026-01-12 | Prefetching | A→B sequence | - | - | 0.59ms | [S3](20260112_phase6_summary.md) |
| 2026-01-11 | Delta Indexing | Initial | - | - | - | [20260111_phase6_delta_indexing.md](20260111_phase6_delta_indexing.md) |

## Recommendations

1. **P10-8 (HNSW)**: Current BruteForce limits QPS to ~150. HNSW would provide 10-100x improvement.
2. **P10-10 (Sidecar Optimization)**: Batch metrics reporting would reduce gRPC overhead.
3. **Separate Deployment**: Running Sidecar on separate host would eliminate resource contention.
