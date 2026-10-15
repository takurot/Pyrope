# Implementation Plan - Issue #63: MemoryCacheStorage: Race Conditions, Stale Usage Accounting, Exposed Internal Buffers

## 1. Overview & Problem Statement
Issue #63 identifies 4 core concurrency, accounting, and security defects in `src/Pyrope.GarnetServer/Model/MemoryCacheStorage.cs`:
1. **TryGet / RemoveEntry race condition & stale usage accounting**: In `TryGet`, when an entry is found to be expired, `RemoveEntry(key, item)` is called. However, `RemoveEntry` ignores `item` and executes `_store.TryRemove(key, out var removed)` under `_sync`. If a concurrent thread executed `Set` with a new value between `TryGet`'s expiration check and `RemoveEntry`'s lock acquisition, the newly written non-expired entry is incorrectly removed, and usage is decremented by the new entry's size rather than the expired entry's size.
2. **Silent rejection on quota breach (`Set` returns `void`)**: When tenant cache memory limit (`CacheMemoryMb`) is breached, `Set` silently returns `void`. Callers cannot detect whether their cache write succeeded or was rejected.
3. **Expired-but-unread entries permanently inflate tenant usage**: Usage is only decremented when an entry is actively read via `TryGet` or overwritten via `Set`. If entries expire without being read, tenant usage remains permanently inflated, permanently starving the tenant of their cache quota.
4. **Internal buffer exposure**: `TryGet` returns `item.Data` by reference without a defensive copy, and `Set` stores `value` without cloning. Callers can mutate the returned or input byte arrays, corrupting the cache contents and breaking immutability guarantees.

## 2. Technical Design

### 2.1 Eliminating Race Conditions in Expiration Removal
- Refactor `RemoveEntry(string key, CacheEntry expectedEntry)` to perform identity verification under `_sync`:
  ```csharp
  private void RemoveEntry(string key, CacheEntry expectedEntry)
  {
      lock (_sync)
      {
          if (_store.TryGetValue(key, out var current) && ReferenceEquals(current, expectedEntry))
          {
              if (_store.TryRemove(key, out _))
              {
                  AdjustUsage(expectedEntry.TenantId, -expectedEntry.SizeBytes);
              }
          }
      }
  }
  ```
  If another thread replaced `key` with a new `CacheEntry`, `ReferenceEquals` evaluates to `false`, leaving the new entry untouched and preserving accurate usage accounting.

### 2.2 `Set` Return Value & Quota Feedback
- Update `ICacheStorage`:
  ```csharp
  bool Set(string key, byte[] value, TimeSpan? ttl = null);
  ```
- In `MemoryCacheStorage.Set`:
  - Return `false` when input validation fails (e.g. `string.IsNullOrEmpty(key)` or `value == null`) or when tenant quota is exceeded after pruning expired entries.
  - Return `true` upon successful storage.
- Update `ResultCache.Set`:
  ```csharp
  public bool Set(QueryKey key, string resultJson, TimeSpan? ttl = null)
  ```
  Returns `bool` indicating whether the entry was accepted by storage.
- Update `MockCacheStorage` in `ResultCacheTests.cs` to return `bool`.

### 2.3 Proactive Pruning of Expired Entries to Prevent Quota Inflation
- Implement `PruneExpiredForTenant(string tenantId, DateTime now)`:
  - Iterates `_store` for entries matching `tenantId` whose `Expiry < now`.
  - Atomically removes them using `_store.TryRemove(new KeyValuePair<string, CacheEntry>(key, entry))` (CAS removal).
  - Decrements tenant usage accordingly.
- In `Set`:
  - Input validation: check `if (string.IsNullOrEmpty(key) || value == null) return false;`.
  - When projected usage exceeds `limitBytes`, invoke `PruneExpiredForTenant(tenantId, now)` before rejecting.
  - Double-subtraction guard: check if `existing` was pruned during `PruneExpiredForTenant`. If no longer in `_store` with identical reference, set `existing = null` before recalculating `projected` and before updating usage.
  - Recalculate projected usage. If within quota, proceed with write and return `true`.
- Implement `int PurgeExpired()` in `ICacheAdmin` and `MemoryCacheStorage`:
  - Scans `_store` and removes all expired entries across all tenants atomically.
- In `GetTenantUsageBytes(tenantId)`:
  - Run `PruneExpiredForTenant(tenantId, DateTime.UtcNow)` before returning usage, guaranteeing reported usage never includes expired entries.
- In `GetAllTenantUsageBytes()`:
  - Run `PurgeExpired()` before constructing the snapshot dictionary.

### 2.4 Defensive Copies for Buffer Isolation
- In `Set`:
  - After validating `value != null`, clone input buffer: `var dataCopy = (byte[])value.Clone();`
  - Store `dataCopy` in `CacheEntry`.
- In `TryGet`:
  - If `string.IsNullOrEmpty(key)`, set `value = null` and return `false`.
  - Clone output buffer: `value = (byte[])item.Data.Clone();`
- External mutations of input or retrieved buffers have zero effect on internal cache state.

## 3. Testing Strategy (TDD)
Write unit tests in `tests/Pyrope.GarnetServer.Tests/Model/MemoryCacheStorageTests.cs`:
1. **Red Test 1: Defensive copy isolation**:
   - `Set` a buffer `[1, 2, 3]`, mutate the local buffer to `[9, 9, 9]`. `TryGet` must still return `[1, 2, 3]`.
   - `TryGet` a buffer `[1, 2, 3]`, mutate the retrieved buffer to `[9, 9, 9]`. Subsequent `TryGet` must still return `[1, 2, 3]`.
2. **Red Test 2: `Set` returns false on quota breach and true on success**:
   - Configure 1MB quota. `Set` 700KB -> returns `true`. `Set` another 700KB -> returns `false`.
3. **Red Test 3: Expired entries do not permanently inflate usage or block future writes**:
   - Configure 1MB quota. `Set` 700KB with 10ms TTL.
   - Wait 50ms (do NOT call `TryGet`).
   - `GetTenantUsageBytes` must return 0.
   - `Set` another 700KB must succeed (returns `true`), verifying proactive pruning unblocks writes.
4. **Red Test 4: Concurrency race between expired removal and new `Set`**:
   - Create expired entry E1.
   - Simulate race: before `RemoveEntry` finishes, a new entry E2 is written.
   - Verify E2 is NOT removed and usage accounts for E2.
5. **Red Test 5: `PurgeExpired` reclaims memory**:
   - Add multiple expired entries across tenants. Call `PurgeExpired()`. Verify returned count and usage is reset.

## 4. Implementation Steps
1. Create feature branch `feature/issue-63-memorycachestorage-races-accounting`.
2. Submit plan to Stage 2 Plan Reviewer subagent.
3. Once approved, write failing tests in `MemoryCacheStorageTests.cs` (Stage 3 Red).
4. Implement fixes in `ICacheStorage.cs`, `ICacheAdmin.cs`, `MemoryCacheStorage.cs`, `ResultCache.cs`, and `MockCacheStorage` (Stage 3 Green).
5. Verify formatting and run quality checks `./scripts/check_quality.sh` (Stage 4).
6. Invoke Stage 5 Code Review subagent.
7. Push, create PR, watch CI, squash merge, update `prompt/PLAN.md` (Stage 6).
