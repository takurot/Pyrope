# P10-8 (IVF-Flat) & CI Fixes Review Request

## Overview
This PR delivers **P10-8: IVF-Flat Index Implementation** as a major performance upgrade over the brute-force index, and includes **Critical CI Stability Fixes** to ensure the pipeline is green.

## Changes Summary

### 1. Vector Search Engine (P10-8)
| File | Role | Key Logic |
|------|------|-----------|
| `IvfFlatVectorIndex.cs` | **New Index Type** | Implements IVF-Flat using K-Means clustering. <br>- **Build**: Trains Centroids, re-assigns buffer vectors to inverted lists.<br>- **Search**: Scans `nProbe` nearest clusters + full Buffer scan.<br>- **Add**: Writes to Buffer (O(1)). |
| `DeltaVectorIndex.cs` | **LSM Architecture** | Managing `Head` (Mutable/BruteForce) and `Tail` (Immutable/IVF).<br>- **Add**: Writes to Head.<br>- **Search**: Merges results from Head & Tail.<br>- **Build**: Moves compaction from Head -> Tail. |

### 2. CI Stability Fixes (Round 3)
| File | Fix |
|------|-----|
| `.github/workflows/ci.yml` | **Auth Fix**: Disabled Auth (`Auth__Enabled=false`) for smoke tests to prevent `VEC_ERR_AUTH`.<br>**Lint Fix**: Removed inline exclusions in favor of config files. |
| `.flake8` ([NEW]) | Config file to exclude generated protobufs (`policy_service_pb2*`) and `venv`. |
| `pyproject.toml` ([NEW]) | Config file for `black` with equivalent exclusions. |
| `tests/smoke_test.py` | Updated default port to `6379` (standard), validated port `3278` logic in CI. |

---

## Review Focus Areas

### 1. K-Means & IVF Logic (`IvfFlatVectorIndex.cs`)
- **Clustering**: Is the `TrainKMeans` implementation (L2-based centroid updates) robust enough for an MVP? (It uses random init, 10 iterations).
- **Metric Handling**: `FindNearestCentroid` uses the Index's metric (Cosine/IP/L2). Is this correct for K-Means assignment, or should assignment always be L2 (Euclidean)?
- **Concurrency**: `ReaderWriterLockSlim` is used. Is the locking granularity correct during `Build()` (WriteLock) vs `Search()` (ReadLock)?

### 2. Delta Index Compaction (`DeltaVectorIndex.cs`)
- **Compaction Logic**: `Build()` scans Head, adds to Tail, and DELETES from Head.
    - *Risk*: Is this atomic? If `Tail.Add` fails or process crashes, data might be lost/duplicated? (Current impl uses a lock but no transaction journal).
- **Merger**: `Search` merges Head/Tail results by ID. Head overwrites Tail. Is this deduplication logic sound?

### 3. CI Configuration
- **Auth Disable**: Is disabling Auth for smoke tests acceptable, or should the smoke test script support API Keys? (Current decision: Disable for minimal smoke test complexity).
- **Config Files**: Confirm `.flake8` and `pyproject.toml` correctly exclude generated code.

---

## Specific Questions for Reviewer

1.  **IVF-Flat**: We currently search the **entire** buffer during `Search()`. If the buffer grows large before a `Build()`, performance degrades to BruteForce. Should we add an auto-trigger for `Build()` based on buffer size?
2.  **K-Means**: We use a simple `Random` init for centroids. Should we switch to `K-Means++` for better convergence, or is this sufficient for P10-8?
3.  **Delta Index**: The `Build()` method holds a WriteLock on the *entire* index during compaction (training + re-indexing). This pauses all reads. Is this acceptable for an MVP, or do we need background compaction with double-buffering?

---

## Test Commands

```bash
# 1. Run Unit Tests (Includes new IVF Tests)
dotnet test tests/Pyrope.GarnetServer.Tests/Pyrope.GarnetServer.Tests.csproj --filter IvfFlatVectorIndexTests

# 2. Run Benchmarks (Triggering Build for IVF)
./scripts/bench_vectors.sh --dataset synthetic --dim 128 --base-limit 10000 --query-limit 1000 --build-index

# 3. Verify CI Formatting/Linting
dotnet format --verify-no-changes
flake8 src/Pyrope.AISidecar
```
