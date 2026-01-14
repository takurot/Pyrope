# Phase 6 Code Review Request (P6-6 ~ P6-12)

## Overview
This branch (`feature/phase6-llm-and-cost`) implements the **Advanced Differentiation** features for the Pyrope AI-Controlled Vector Database, focusing on Cost-Aware routing and LLM integration.

## Changes Summary

### C# (Garnet Server)
| File | Changes |
|------|---------|
| `VectorCommandSet.cs` | P6-6 Cost-aware query routing, P6-7 Semantic TTL integration |
| `TenantConfig.cs` | Added `MonthlyBudget` property to `TenantQuota` |
| `TenantQuotaEnforcer.cs` | Added `RecordCost()`, `IsOverBudget()` methods with monthly tracking |
| `SemanticClusterRegistry.cs` | P6-7 Cluster heat tracking, `RecordWrite()`, `GetRecommendedTTL()` |
| `SearchOptions.cs` | Added `NProbe`, `EfSearch` optional parameters |
| `CanonicalKeyMap.cs` | **[NEW]** P6-9 Query alias mapping with TTL and confidence |

### Python (AI Sidecar)
| File | Changes |
|------|---------|
| `llm_worker.py` | **[NEW]** P6-8 Gemini client + P6-12 Rate limiting, token tracking |
| `llm_dispatcher.py` | **[NEW]** P6-10 Prefetch dispatcher, P6-11 TTL advisor |
| `prompts.py` | **[NEW]** Prompt templates for LLM tasks |
| `server.py` | LLMWorker integration, asyncio event loop management |
| `requirements.txt` | Added `google-generativeai` |

### Tests
| File | Coverage |
|------|----------|
| `CostAwareQueryTests.cs` | P6-6 Cost degradation logic |
| `SemanticClusterRegistryTests.cs` | P6-7 Heat tracking and TTL |
| `test_llm_worker.py` | P6-8/12 LLMWorker mocked tests |
| `test_llm_dispatcher.py` | P6-10/11 Dispatcher and TTL advisor |

---

## Review Focus Areas

### 1. Thread Safety (Critical)
- `TenantQuotaEnforcer._costStates` uses `ConcurrentDictionary` with internal locks
- `SemanticClusterRegistry._writeHeat` uses similar pattern
- **Question**: Is there risk of deadlock between `lock (state.Sync)` across these components?

### 2. Memory Management
- `LLMWorker._request_timestamps` and `_token_window` use `deque(maxlen=1000)`
- `CanonicalKeyMap` stores entries without automatic cleanup (needs periodic `CleanupExpired()`)
- **Question**: Should we add a background cleanup task?

### 3. Error Handling
- `LLMWorker` catches exceptions but re-queues on rate limit - potential infinite loop?
- `VectorCommandSet` cost calculation happens before search - failure path unclear

### 4. Performance Concerns
- P6-7: `FindNearestCluster()` is called on every write (O(K) per write)
- Cost calculation uses `CostCalculator.EstimateSearchCost()` on every search

### 5. API Design
- `CanonicalKeyMap.TryGetCanonical()` returns both `canonicalHash` and `confidence` via out params - consider tuple return?
- `LLMWorker.submit_task()` has optional `priority` param not implemented

---

## Specific Questions for Reviewer

1. **P6-6**: Cost degradation reduces `MaxScans` by 50% when over budget. Is this too aggressive?
   
2. **P6-7**: Cluster heat threshold is hardcoded at 10 writes/minute. Should this be configurable?

3. **P6-12**: Token estimation uses `len(prompt.split()) * 1.3`. Is this accurate enough for budgeting?

4. **General**: The `google.generativeai` package shows a deprecation warning. Should we migrate to `google.genai` now?

---

## Test Commands

```bash
# C# Tests
dotnet test Pyrope.sln

# Python Tests
cd src/Pyrope.AISidecar
source venv/bin/activate
python -m unittest discover -s tests -p "test_*.py"

# Benchmark
./scripts/bench_vectors.sh --dataset synthetic --dim 128 --base-limit 10000 --query-limit 1000 --api-key test --http http://localhost:5000 --admin-api-key admin
```

---

## Benchmark Results (Reference)

| Metric | Without Sidecar | With Sidecar |
|--------|-----------------|--------------|
| Write Throughput | 16,268 vec/s | 10,615 vec/s |
| Search QPS | 167 | 123 |
| P50 Latency | 22ms | 28ms |
| P99 Latency | 42ms | 128ms |

*Note: Performance degradation is expected due to gRPC overhead and resource contention on local machine.*
