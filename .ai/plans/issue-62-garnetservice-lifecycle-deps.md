# Implementation Plan - Issue #62: GarnetService Lifecycle & Dependency Hardening (Revision 2)

## 1. Overview & Problem Statement
Issue #62 identifies several lifecycle and dependency defects in `src/Pyrope.GarnetServer/Services/GarnetService.cs`:
1. **Swallowed startup exceptions**: `StartAsync` catches all exceptions during `_server.Start()`, logs a console warning, and returns `Task.CompletedTask`. The .NET generic host considers `GarnetService` started, leaving the process running in a broken zombie state without an active RESP server.
2. **Double-dispose hazard**: `StopAsync` calls `_server.Dispose()`, and host disposal subsequently calls `Dispose()`. Without a `_disposed` guard, `_server.Dispose()` is called twice during shutdown.
3. **Null dependencies passed to vector command handlers**: In `RegisterCommands()`, `VEC.ADD`, `VEC.UPSERT`, `VEC.DEL`, and `VEC.STATS` are initialized with positional null arguments, omitting `clusterRegistry`, `logger`, `metricsCollector`, and `billingMeter`. For example, `clusterRegistry.RecordWrite` is never invoked on `VEC.ADD`/`VEC.UPSERT` because `clusterRegistry` is null.
4. **Missing constructor argument validation**: `GarnetService` accepts 13 required dependencies without any null validation, delaying failures until requests trigger null reference exceptions.

## 2. Technical Design

### 2.1 Exception Propagation in `StartAsync`
- Wrap `_server.Start()` in `try ... catch (Exception ex)`.
- Log the error using `_logger.LogError(ex, "Failed to start GarnetServer: {Message}", ex.Message)`.
- Re-throw the exception (`throw;`) so .NET Generic Host fails fast and shuts down cleanly rather than remaining in an unrecoverable zombie state.

### 2.2 Thread-Safe Single-Disposal Pattern
- Introduce `private volatile bool _disposed;` and `private readonly object _disposeLock = new();`.
- Implement `Dispose()`:
  ```csharp
  public void Dispose()
  {
      if (_disposed) return;
      lock (_disposeLock)
      {
          if (_disposed) return;
          _disposed = true;
          _server?.Dispose();
      }
      GC.SuppressFinalize(this);
  }
  ```
- In `StopAsync`: delegate directly to `Dispose()`.
  ```csharp
  public Task StopAsync(CancellationToken cancellationToken)
  {
      Dispose();
      return Task.CompletedTask;
  }
  ```
- Calling `StopAsync` followed by `Dispose()` ensures `_server.Dispose()` is executed exactly once without double-dispose hazards.

### 2.3 Dependency Injection & Command Registration Hardening
- In `GarnetService` constructor, validate every required parameter using `ArgumentNullException.ThrowIfNull(...)`.
- In `VectorCommandSet.cs`, expose internal diagnostic properties:
  ```csharp
  internal SemanticClusterRegistry? ClusterRegistry => _clusterRegistry;
  internal ILogger? Logger => _logger;
  internal IMetricsCollector? Metrics => _metrics;
  internal IBillingMeter? BillingMeter => _billingMeter;
  ```
- In `RegisterCommands()`, eliminate positional argument ambiguity by using **C# named arguments** across all command registrations:
  ```csharp
  _server.Register.NewCommand("VEC.ADD", CommandType.ReadModifyWrite, new VectorCommandSet(
      commandType: VectorCommandType.Add,
      resultCache: _resultCache,
      policyEngine: _policyEngine,
      metrics: _metricsCollector,
      lshService: _lshService,
      quotaEnforcer: _quotaEnforcer,
      tenantAuthenticator: _tenantAuthenticator,
      sloGuardrails: _sloGuardrails,
      clusterRegistry: _clusterRegistry,
      canonicalKeyMap: _canonicalKeyMap,
      prefetcher: _prefetcher,
      prefetchQueue: _prefetchQueue,
      logger: _logger,
      billingMeter: _billingMeter
  ), new RespCommandsInfo { Command = (RespCommand)VectorCommandSet.VEC_ADD, Name = "VEC.ADD" });
  ```
  Apply this consistently to `VEC.ADD`, `VEC.UPSERT`, `VEC.DEL`, `VEC.SEARCH`, and `VEC.STATS`.

## 3. Testing Strategy (TDD)
Create `tests/Pyrope.GarnetServer.Tests/Services/GarnetServiceTests.cs`:
1. **Red Test 1: Constructor null validation**: Verify `GarnetService` throws `ArgumentNullException` when any of the 13 required dependencies is null.
2. **Red Test 2: StartAsync exception rethrow via deterministic port collision**:
   - Bind a `TcpListener` on `IPAddress.Loopback` with port 0 (ephemeral port) and call `listener.Start()`.
   - Extract the bound port and construct `GarnetService` with args `["--port", boundPort.ToString(), "--bind", "127.0.0.1"]`.
   - Assert `await Assert.ThrowsAnyAsync<Exception>(() => service.StartAsync(CancellationToken.None))` catches the propagated startup failure.
   - Stop `listener`.
3. **Red Test 3: Idempotent disposal (no double-dispose)**:
   - Call `await service.StopAsync(CancellationToken.None)` then `service.Dispose()`, and repeat `service.Dispose()`.
   - Assert all calls succeed cleanly without exception.
4. **Red Test 4: True dependency verification for command handlers**:
   - Extract command sets registered in `GarnetService` (or test helper method / internal factory `CreateCommandSet(VectorCommandType)`).
   - Assert that handlers for `VEC.ADD`, `VEC.UPSERT`, and `VEC.DEL` have non-null `ClusterRegistry`, `BillingMeter`, `Metrics`, and `Logger`.
   - On unpatched code, `ClusterRegistry` and `Logger` are null, making this a true failing Red test.

## 4. Implementation Steps
1. Create feature branch `feature/issue-62-garnetservice-lifecycle-deps`.
2. Submit Revision 2 of plan to Plan Reviewer subagent (Stage 2).
3. Once approved, write failing tests in `GarnetServiceTests.cs` (Stage 3 Red).
4. Update `GarnetService.cs` and `VectorCommandSet.cs` with validations, exception propagation, volatile disposal guard, named arguments, and full command dependencies (Stage 3 Green).
5. Run `dotnet format` and `./scripts/check_quality.sh` (Stage 4).
6. Invoke Stage 5 Code Review subagent.
7. Push, create PR, watch CI, squash merge, update `prompt/PLAN.md` (Stage 6).
