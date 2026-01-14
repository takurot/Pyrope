# Code Review – Phase 6 LLM & Cost

## Step-by-step plan
1) Read scope & focus from `review/codex_review_prompt.md` and roadmap in `prompt/PLAN.md` (Phase 6: cost-aware routing, semantic TTL, LLM worker/dispatcher, canonical aliases).
2) Review C# core changes for thread-safety, cost/TTL logic, and API surface: `VectorCommandSet`, `TenantQuotaEnforcer`, `SemanticClusterRegistry`, `SearchOptions`, `TenantConfig`, `CanonicalKeyMap`.
3) Review Python sidecar for concurrency, budgeting, and error handling: `llm_worker.py`, `llm_dispatcher.py`, `prompts.py`, `server.py`, and dependency changes.
4) Check new tests (`CostAwareQueryTests`, `SemanticClusterRegistryTests`, `test_llm_worker.py`, `test_llm_dispatcher.py`) for coverage gaps and reliability.
5) Summarize risks, answer the prompt’s specific questions, and note untested areas.

## Findings (ordered by severity)
- [High] Event loop mismatch will crash LLM worker at runtime: `LLMWorker` creates its queue before any loop exists (`src/Pyrope.AISidecar/llm_worker.py:20-27`), but `serve()` starts the worker on a brand-new event loop in a different thread (`src/Pyrope.AISidecar/server.py:151-162`). On Python ≥3.11, `asyncio.Queue()` binds to the creating loop; awaiting `queue.get()` from another loop raises "Future attached to a different loop", preventing the worker from starting. Build the queue after the target loop is set (e.g., inside `start()` using `asyncio.get_running_loop()` or pass the loop into the constructor and use it consistently) and avoid crossing threads without thread-safe calls.
- [High] LLM worker cannot shut down cleanly and leaves a runaway loop: `serve()` spins `loop.run_forever()` in a daemon thread with no stop signal (`src/Pyrope.AISidecar/server.py:159-162`), while `LLMWorker.stop()` only flips a flag (`src/Pyrope.AISidecar/llm_worker.py:69-72`) without cancelling the `_process_queue` task or waking the pending `queue.get()`. The process will hang on exit and leak the worker thread. Keep a handle to the task, push a sentinel to unblock `queue.get()`, and call `loop.call_soon_threadsafe(loop.stop)` during server shutdown.
- [Medium] Cost budgeting bypasses the injected time provider and loses determinism: `RecordCost` uses `DateTimeOffset.UtcNow` and stores only the month number (`src/Pyrope.GarnetServer/Services/TenantQuotaEnforcer.cs:94-117`). Other quota windows use `ITimeProvider`, so tests or simulations cannot control budget windows, and clock skew between components can make budgets drift. Use `_timeProvider.GetUnixTimeSeconds()` (or return a UTC `DateTimeOffset`) and track year/month to reset accurately in long-running processes.
- [Medium] CanonicalKeyMap is implemented but never used and lacks lifecycle management. No call sites resolve aliases before cache lookup (no references beyond its own file), so P6-9 functionality is effectively absent. Entries also rely on manual `CleanupExpired()` and will grow unbounded under sustained alias writes. Wire the map into the search path (e.g., resolve before L0/L1 cache checks) and schedule periodic cleanup or size limits.
- [Medium] Rate-limit retry loop can spin indefinitely and starve newer tasks: when rate-limited, `_process_queue` re-queues the same prompt after `await asyncio.sleep(1)` without a retry cap or backoff (`src/Pyrope.AISidecar/llm_worker.py:131-139`). A prolonged provider throttle will keep recycling the same item and block the queue. Track attempts with a max retry/backoff or drop with a clear error to prevent starvation.
- [Medium] Cost-aware routing adjusts only `MaxScans`; new knobs `NProbe`/`EfSearch` are unused by indexes. The heuristic cost estimator ignores `topK`, so many searches may never trigger degradation, and responses expose `BudgetAdjustment` even when search fails. Consider propagating `NProbe`/`EfSearch` into index implementations and basing cost on query parameters to align with P6-6 goals.

## Responses to specific questions
1) P6-6 degradation (50% MaxScans) is coarse; consider scaling by remaining budget or clamping to a minimum/maximum rather than a fixed halving, and prefer adjusting search knobs (nprobe/efSearch) that better reflect actual cost.
2) P6-7 threshold (10 writes/min) should be configurable per index/tenant and possibly derived from centroid count; expose it via config or SLO guardrails.
3) P6-12 token estimation via `len(prompt.split()) * 1.3` is rough; prefer provider-reported usage or a tokenizer-based estimate to avoid undercounting long prompts/JSON payloads.
4) `google-generativeai` deprecation: plan a migration path to `google-genai` (or current package) while keeping backward compatibility; add a feature flag or adapter to switch clients.

## Testing
- Not run in this review (no commands executed). Recommend running `dotnet test Pyrope.sln` and `python -m unittest discover -s src/Pyrope.AISidecar/tests` after fixes.
