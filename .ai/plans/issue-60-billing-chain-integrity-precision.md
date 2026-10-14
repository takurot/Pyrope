# Implementation Plan - Issue #60: BillingLogStore chain integrity broken + float precision loss

## 1. Background & Problem Analysis
Issue #60 identifies multiple correctness and data integrity bugs across billing and metrics:
1. **Chain Verification Broken by In-Memory Trimming (`BillingLogStore.cs`)**:
   - `VerifyChain(string tenantId)` always initializes `prevHash = GenesisHash`.
   - When the in-memory queue exceeds `MaxInMemoryEntries` (default 10,000, or smaller in tests/constrained environments), older entries are dequeued from the front.
   - When verified, the first remaining entry `entries[0]` has `PrevHash` pointing to an evicted entry rather than `GenesisHash`.
   - `VerifyChain` hashes `GenesisHash` with `entries[0]`'s payload, fails to match `entries[0].Hash`, and returns `false` permanently.
2. **Loss of Hash Continuity on Process Restart (`BillingLogStore.cs`)**:
   - On server restart, `BillingLogStore` does not reload existing entries or `LastHash` from `LogPath`.
   - When a new snapshot is appended after restart, `state.LastHash` is null, so it falls back to `GenesisHash`.
   - The on-disk JSONL log now contains discontinuous chains (a new genesis block mid-log for existing tenants).
3. **Floating-Point Precision Loss in Financial Accumulation (`BillingUsage.cs`, `BillingLogEntry.cs`, `IBillingMeter.cs`, `BillingMeter.cs`, `BillingOptions.cs`)**:
   - `ComputeCostUnits` and `ComputeSeconds` use IEEE 754 `double`, which suffers from binary floating-point rounding errors (e.g., `0.1 + 0.2 != 0.3`) over millions of accumulated vector queries.
   - Financial and billing meters should use `decimal` for exact 128-bit decimal arithmetic.
4. **Prometheus Histogram `_sum` Hardcoded to 0 (`MetricsCollector.cs`)**:
   - `MetricsCollector.GetStats()` outputs `vector_search_latency_ms_sum 0` with a comment `// Sum not implemented yet`.
   - Prometheus metrics scrapers and SLO dashboards cannot calculate average latency or rate-of-sum.

## 2. Technical Design & Implementation

### 2.1 BillingLogStore Chain Verification Fix
In `src/Pyrope.GarnetServer/Services/BillingLogStore.cs`:
- In `VerifyChain(string tenantId)`:
  - If `entries.Length == 0`, return `true`.
  - Initialize `prevHash = entries[0].PrevHash`.
  - For each entry in `entries`:
    - Verify `entry.PrevHash == prevHash`.
    - Compute `expected = ComputeHash(prevHash, payload)`.
    - Verify `entry.Hash == expected`.
    - Update `prevHash = entry.Hash`.
  - This verifies cryptographic integrity and unbroken linkage across all in-memory entries regardless of whether older entries have been evicted.

### 2.2 BillingLogStore State Recovery on Startup
In `src/Pyrope.GarnetServer/Services/BillingLogStore.cs`:
- Add `LoadInitialState()` called from constructor:
  - If `!string.IsNullOrWhiteSpace(_options.LogPath) && File.Exists(_options.LogPath)`:
    - Read lines under `_fileLock`.
    - Deserialize each JSON line into `BillingLogEntry`.
    - For each entry, populate `_states[entry.TenantId]`:
      - `state.Entries.Enqueue(entry);`
      - `state.LastHash = entry.Hash;`
      - Trim `state.Entries` while `Count > _options.MaxInMemoryEntries`.
  - When the first `AppendSnapshot` occurs after process restart, `prevHash` picks up `state.LastHash` from disk, preserving cryptographic continuity.

### 2.3 Float to Decimal Transition for Billing Accumulation
- In `src/Pyrope.GarnetServer/Model/BillingUsage.cs`:
  - Change `ComputeCostUnits` and `ComputeSeconds` from `double` to `decimal`.
- In `src/Pyrope.GarnetServer/Model/BillingLogEntry.cs`:
  - Change `ComputeCostUnits` and `ComputeSeconds` from `double` to `decimal`.
- In `src/Pyrope.GarnetServer/Services/BillingOptions.cs`:
  - Change `CostUnitSeconds` from `double` to `decimal` (default `1.0m`).
- In `src/Pyrope.GarnetServer/Services/IBillingMeter.cs`:
  - Add `void RecordCompute(string tenantId, decimal costUnits);`
  - Overload `void RecordCompute(string tenantId, double costUnits);` for backward compatibility.
- In `src/Pyrope.GarnetServer/Services/BillingMeter.cs`:
  - `TenantUsageState.ComputeCostUnits` and `ComputeSeconds` changed to `decimal`.
  - Arithmetic performed in decimal: `var computeSeconds = costUnits * _options.CostUnitSeconds;`.
- In `src/Pyrope.GarnetServer/Services/BillingLogStore.cs`:
  - Update `BuildPayload` to accept `decimal computeCostUnits, decimal computeSeconds` formatted with `CultureInfo.InvariantCulture`.
- In `src/Pyrope.GarnetServer/Controllers/BillingController.cs`:
  - Update fallback usage instantiation to pass `0m, 0m`.
- In `src/Pyrope.GarnetServer/Program.cs`:
  - Parse `CostUnitSeconds` as decimal with `decimal.TryParse`.

### 2.4 MetricsCollector Prometheus Histogram Sum
In `src/Pyrope.GarnetServer/Services/MetricsCollector.cs`:
- Add `private double _latencySumMs;`
- In `RecordSearchLatency(TimeSpan duration)`:
  - Atomically accumulate `ms = duration.TotalMilliseconds` using CAS loop with `Interlocked.CompareExchange(ref _latencySumMs, computed, initial)`.
- In `GetStats()`:
  - Output `vector_search_latency_ms_sum {Volatile.Read(ref _latencySumMs).ToString(CultureInfo.InvariantCulture)}`.
- In `GetSnapshot()`:
  - Include latency sum in `MetricsSnapshot` (or preserve snapshot struct).
- In `Reset()`:
  - Reset `Interlocked.Exchange(ref _latencySumMs, 0.0)`.

## 3. Test Strategy (TDD)
1. **BillingLogStore Chain Integrity Tests (`BillingLogStoreTests.cs`)**:
   - `VerifyChain_WhenEntriesEvicted_StillVerifiesIntegrity`: Set `MaxInMemoryEntries = 3`, append 10 snapshots. Verify `VerifyChain` returns `true`.
   - `VerifyChain_WhenTampered_ReturnsFalse`: Tamper with an entry payload or hash and assert `VerifyChain` returns `false`.
   - `ProcessRestart_MaintainsHashContinuity`: Create store with a temporary log file, append snapshots, simulate restart with a new `BillingLogStore` instance on the same file, append further snapshots. Verify `entry.PrevHash` matches the last persisted hash from before restart and `VerifyChain` returns `true`.
2. **Decimal Precision Tests (`BillingMeterTests.cs`)**:
   - `RecordCompute_AccumulatesWithDecimalPrecision`: Accumulate multiple fractional amounts (e.g., 0.1m, 0.2m repeated 10 times) and verify exact decimal result without floating-point drift.
3. **MetricsCollector Histogram Sum Tests (`MetricsCollectorTests.cs`)**:
   - `RecordSearchLatency_AccumulatesHistogramSum`: Record known latencies (1.5ms, 2.5ms) and verify `vector_search_latency_ms_sum 4` in `GetStats()`.
   - `Reset_ClearsHistogramSum`: Verify `vector_search_latency_ms_sum 0` after reset.

## 4. Acceptance Criteria
- Unit tests pass.
- `./scripts/check_quality.sh` passes 100%.
- Chain verification works properly even when queue trimming occurs.
- On-disk continuity is preserved across process restarts.
- Financial metrics use exact `decimal` precision.
- Prometheus histogram sum reports true accumulated latency.
