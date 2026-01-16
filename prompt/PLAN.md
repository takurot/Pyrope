# Implementation Roadmap: AI-Controlled Vector DB

Based on `prompt/SPEC.md`, this document outlines the step-by-step implementation plan.
Tasks are designed to be PR-sized units (1-3 days work) and allow parallel execution across **Core (C#)** and **ML (Python)** tracks.

## Legend
*   **[Core]**: Garnet Extension, C# Logic, Vector Search Engine
*   **[ML]**: AI Controller, Python Sidecar, Training Pipeline
*   **[Ops]**: CI/CD, Docker, Benchmarking, Metrics
*   **[Security]**: Authentication, Authorization, Audit Logging
*   **[API]**: Data Plane (RESP) and Control Plane (HTTP) APIs

## Status
*   [ ] Not Started
*   [/] In Progress
*   [x] Completed

---

## Phase 0: Foundation & Scaffolding
*Goal: Working development environment where Garnet and AI Sidecar can communicate.*

| ID | Track | Task | Dependencies | Status |
|----|-------|------|--------------|--------|
| **P0-1** | [Core] | **Project Skeleton & Garnet Hosting**<br>Set up C# solution, integrate `Microsoft.Garnet`, create `GarnetServer` console app. Ensure basic Redis commands work. | - | [x] |
| **P0-2** | [ML] | **AI Sidecar Skeleton (GRPC)**<br>Create Python project with `grpcio`. Define `.proto` for `PolicyService`. Implement dummy "Echo" policy. | - | [x] |
| **P0-3** | [Ops] | **Docker Compose Environment**<br>Containerize P0-1 and P0-2. Configure network. Verify GRPC connectivity from Garnet to Python. | P0-1, P0-2 | [x] |
| **P0-4** | [Core] | **Custom Command Registry**<br>Implement `VectorCommandSet` class inheriting from `CustomRawStringFunctions`. Stub `VEC.ADD`, `VEC.SEARCH` placeholders. Register commands in `Program.cs`. **Verification**: `CommandRegistryTests` pass (commands accepted). | P0-1 | [x] |
| **P0-5** | [Core] | **Tenant & Index Data Model**<br>Implement `tenant_id`, `index_name` namespace scheme. Define `IndexConfig` structure (dim, metric, index_factory, train config). Store in Garnet KV via `IndexMetadataManager`. **Verification**: `IndexMetadataTests` pass (serialize/store/retrieve). | P0-1 | [x] |

---

## Phase 1: Basic Vector Search (No Cache)
*Goal: Ability to add, upsert, delete and search vectors using FAISS without AI features.*

| ID | Track | Task | Dependencies | Status |
|----|-------|------|--------------|--------|
| **P1-1** | [Core] | **FAISS Interop / C# Vector Lib**<br>Integrate FAISS (via C++ interop or C# port) or a lightweight C# vector search lib (for MVP). Define `IVectorIndex` interface supporting cosine/ip/l2. | P0-1 | [x] |
| **P1-2** | [Core] | **Implement `VEC.ADD` & `VEC.UPSERT`**<br>Parse arguments (blob/json). Store vector to `VectorStore` (disk/memory). Update in-memory Index. Support `META <json>`, `tags`, `numeric_fields`. | P1-1, P0-4, P0-5 | [x] |
| **P1-2-1** | [Ops] | **CI/CD Skeleton**<br>Set up CI to run `dotnet test` on PRs. Enhanced with format check, code coverage, Python sidecar lint/test, E2E smoke test (conditional), and security scanning. | P1-2 | [x] |
| **P1-3** | [Core] | **Implement `VEC.DEL`**<br>Logical deletion with `deleted: bool` flag. Support epoch/version management for cache invalidation. | P1-2 | [x] |
| **P1-4** | [Core] | **Implement `VEC.SEARCH` (Brute Force)**<br>Basic Flat Search implementation. Support `TOPK`, `FILTER` (tag-based). Return RESP array with IDs, Scores, and optional Meta. | P1-2 | [x] |
| **P1-5** | [Core] | **Error Code System**<br>Implement standardized error codes: `VEC_OK`, `VEC_ERR_DIM`, `VEC_ERR_NOT_FOUND`, `VEC_ERR_QUOTA`, `VEC_ERR_BUSY`. | P1-4 | [x] |
| **P1-6** | [Ops] | **Vector Benchmarking Data & Tool**<br>Script to load SIFT1M/Glove datasets. Benchmark baseline latency/QPS of P1-4. | P1-4 | [x] |

---

## Phase 2: Cache Layer & Hot Path Rules
*Goal: Implement the "Cache" part of Vector DB with static policies.*

| ID | Track | Task | Dependencies | Status |
|----|-------|------|--------------|--------|
| **P2-1** | [Core] | **QueryKey Generation (Level 0)**<br>Implement `QueryKey` hashing (Exact Match: vector hash + filter + topK + metric). | P1-4 | [x] |
| **P2-2** | [Core] | **Result Cache (L0)**<br>Implement `ResultCache` (QueryKey → topK results). Use Garnet's key-value store. Include `epoch` field for invalidation. | P2-1 | [x] |
| **P2-3** | [Core] | **Hot Path Policy Engine**<br>Create `PolicyCheck` hook in `VEC.SEARCH`. Implement static rules (e.g., "Always Cache", "TTL=60s"). Thread-safe atomic swap for policy updates. | P2-2 | [x] |
| **P2-4** | [Core] | **Epoch-Based Cache Invalidation**<br>Increment `version_epoch` on index updates. Invalidate cache entries with stale epoch. | P2-2, P1-3 | [x] |
| **P2-5** | [Core] | **Cache Hit/Miss Telemetry**<br>Add metrics: `cache_hit`, `cache_miss`, `latency_p99`. Expose via Prometheus-style endpoint. Include `cache_eviction_total` with reason. | P2-3 | [x] |
| **P2-6** | [Core] | **Semantic Caching (L1: Quantized QueryKey)**<br>Implement SimHash quantization (512dim→64bit). TopK rounding (5/10/20/50/100). | P2-1 | [x] |

---

## Phase 3: Control Plane API (HTTP)
*Goal: Expose management APIs for index, tenant, cache, and health operations.*

| ID | Track | Task | Dependencies | Status |
|----|-------|------|--------------|--------|
| **P3-1** | [API] | **Index Management API**<br>`POST /v1/indexes`, `POST /v1/indexes/{id}/build`, `POST /v1/indexes/{id}/snapshot`, `POST /v1/indexes/{id}/load`, `GET /v1/indexes/{id}/stats`. | P1-2 | [x] |
| **P3-2** | [API] | **Tenant Management API**<br>`POST /v1/tenants`, `GET /v1/tenants/{id}/quotas`, `PUT /v1/tenants/{id}/quotas`. | P0-5 | [x] |
| **P3-3** | [API] | **Cache Management API**<br>`GET /v1/cache/policies`, `PUT /v1/cache/policies`, `POST /v1/cache/flush`, `POST /v1/cache/invalidate`. | P2-3 | [x] |
| **P3-4** | [API] | **Health & Metrics API**<br>`GET /v1/health`, `GET /v1/metrics (Prometheus format)`. | P2-5 | [x] |
| **P3-5** | [API] | **Tracing Support**<br>Implement `request_id` propagation. Add `TRACE` option to VEC.SEARCH for latency breakdown (cache→faiss→policy). | P2-5 | [x] |

---

## Phase 4: AI Controller Integration (Warm Path)
*Goal: Connect the AI brain to dynamically update the Hot Path rules.*

| ID | Track | Task | Dependencies | Status |
|----|-------|------|--------------|--------|
| **P4-1** | [Core] | **Metrics Aggregation for Sidecar**<br>Collect `QPS`, `MissRate`, `Latency`, `CPU/GPU utilization` in Garnet. Periodically push/pull stats to AI Sidecar via GRPC. | P2-5, P0-3 | [x] |
| **P4-2** | [ML] | **Feature Engineering Pipeline**<br>Implement feature extraction: query features (norm, topK, filter type), system features (QPS, queue depth), history features (hit rate, revisit interval). | P4-1 | [x] |
| **P4-3** | [ML] | **Simple Heuristic Policy (Warm Path)**<br>Implement logic in Python: "If MissRate > X, increase TTL". Return updated Policy config (admit, ttl, evict_priority) to Garnet. | P0-2, P4-2 | [x] |
| **P4-4** | [Core] | **Policy Update Mechanism**<br>Receive Policy config from Sidecar. Thread-safe update of Hot Path parameters (Atomic swap of Lookup Table/Bloom Filter). | P4-1, P4-3 | [x] |
| **P4-5** | [ML] | **Offline Datagen Pipeline**<br>Logger to save query/decision logs to disk for training. Include tenant, query stats, system load, decision outcome. | P4-1 | [x] |
| **P4-6** | [ML] | **Fallback Guardrail**<br>If Warm Path response > 50ms timeout, fallback to Hot Path cached rules or LRU. Monitor `ai_fallback_total`. | P4-4 | [x] |

---

## Phase 5: Production Hardening & Multi-tenancy
*Goal: SLOs, Rate Limiting, Multi-tenancy, and Security.*

| ID | Track | Task | Dependencies | Status |
|----|-------|------|--------------|--------|
| **P5-1** | [Core] | **Multi-tenancy Isolation**<br>Enforce `tenant_id` prefix on all keys/indexes. Implement namespace isolation. | P0-5 | [x] |
| **P5-2** | [Core] | **Tenant Quotas & QoS**<br>Implement QPS limits, concurrent execution limits, cache memory limits per tenant. | P5-1, P3-2 | [x] |
| **P5-3** | [Core] | **Noisy Neighbor Mitigation**<br>Priority-based scheduling. On P99 breach: degrade low-priority tenant params, tighten admission. | P5-2, P2-5 | [x] |
| **P5-4** | [Core] | **SLO Guardrails (Shedding)**<br>If `P99 > Target`, auto-degrade search params (lower nprobe/efSearch). Implement `CACHE_HINT=force` SLO mode. | P2-5 | [x] |
| **P5-5** | [Security] | **Authentication**<br>Implement API Key authentication. mTLS for inter-service communication. | P3-1 | [x] |
| **P5-6** | [Security] | **Authorization (RBAC)**<br>Implement roles: `tenant_admin`, `operator`, `reader`. Per-tenant index/operation permissions. | P5-5 | [x] |
| **P5-7** | [Security] | **Audit Logging**<br>Log all management operations: index create/delete, snapshot, policy change, model switch, quota change. | P5-6 | [x] |
| **P5-8** | [Ops] | **Load Testing & Tuning**<br>Run heavy concurrent load. Tune GC, thread pool, cache sizes. Validate SLO compliance. | All | [x] |

---

## Phase 6: Advanced Differentiation
*Goal: Implement the "Wow" features defined in SPEC.md Section 17.*

| ID | Track | Task | Dependencies | Status |
|----|-------|------|--------------|--------|
| **P6-1** | [Core] | **Delta Indexing (LSM Strategy)**<br>Implement `Head Index` (HNSW/Flat, realtime updates) + `Tail Index` (IVF-PQ, immutable). Background merge task. Search merges Head + Tail results. | P1-2 | [x] |
| **P6-2** | [Core] | **Semantic Caching (L2: Cluster-Based)**<br>Cluster query embeddings (128-256 clusters). Cache representative results per cluster. Skip FAISS if similarity > threshold. | P2-6 | [x] |
| **P6-3** | [Core] | **Cost-Aware Semantic Cache**<br>Implement Proxy Cost Metric: `nprobe * (avg_cluster_size / total_vectors)`. Adjust cache hit threshold based on query cost—higher cost queries tolerate looser matching. | P6-2 | [x] |
| **P6-4** | [ML] | **Predictive Prefetching (FIM)**<br>Implement Frequent Itemset Mining on query logs. Generate "Next Query" prediction table. Store in Garnet KV. | P4-5 | [x] |
| **P6-5** | [Core] | **Prefetch Execution**<br>Read "Next Query" table on search completion. Queue background prefetch for predicted queries during idle time. | P6-4 | [x] |
| **P6-6** | [Core] | **Cost-Aware Query Routing**<br>Predict query cost (FAISS time, result size, tenant budget remaining). Auto-adjust nprobe/efSearch/topK when cost exceeds budget. Include adjustment in response (transparency). | P4-2, P5-2 | [x] |
| **P6-7** | [Core] | **Semantic TTL**<br>Detect drift (cluster heat from concentrated data additions). Auto-shorten TTL for affected cluster's QueryKeys. | P6-2, P4-4 | [x] |
| **P6-8** | [ML] | **LLM Worker (Gemini) Skeleton**<br>Consume async queue. Implement prompt templates for normalization/prefetch/eviction. Parse structured output. Apply head-query/cost filters. | P4-5 | [x] |
| **P6-9** | [Core] | **CanonicalKey Alias Map**<br>Add `CanonicalKeyMap` in Garnet KV. Resolve alias before cache lookup. Apply TTL and confidence thresholds. | P2-1, P2-2, P6-8 | [x] |
| **P6-10** | [Core] | **LLM Prefetch Dispatcher**<br>Convert LLM predictions into prefetch jobs. Reuse P6-5 background search fill. | P6-5, P6-8 | [x] |
| **P6-11** | [Core] | **LLM Eviction/TTL Overrides**<br>Apply LLM advisory TTL/priority in eviction scoring with guardrails. | P2-4, P6-8 | [x] |
| **P6-12** | [Ops] | **LLM Budgeting & Metrics**<br>Rate-limit Gemini calls, track cost, expose metrics/alerts. | P3-4, P6-8 | [x] |
| **P6-13** | [ML] | **Gemini Cache Control Integration**<br>Replace HeuristicPolicyEngine with LLMPolicyEngine. Use Gemini to generate TTL, admission threshold, and eviction priority based on system metrics. Implement fallback to heuristic on LLM failure. Feature flag: `LLM_POLICY_ENABLED`. | P6-8, P6-12 | [x] |

---

## Phase 7: Billing & Metering
*Goal: Enable monetization with accurate usage tracking.*

| ID | Track | Task | Dependencies | Status |
|----|-------|------|--------------|--------|
| **P7-1** | [Core] | **Request Metering**<br>Count search requests per tenant. Include cache hit/miss breakdown. | P5-1, P2-5 | [ ] |
| **P7-2** | [Core] | **Compute Metering**<br>Estimate CPU/GPU seconds per request using Proxy Cost Metric. Aggregate per tenant. | P6-3, P5-1 | [ ] |
| **P7-3** | [Core] | **Storage Metering**<br>Track vector storage bytes, snapshot storage per tenant. | P5-1 | [ ] |
| **P7-4** | [API] | **Billing API**<br>`GET /v1/billing/usage`. Return requests, compute, storage, cache memory by tenant. | P7-1, P7-2, P7-3 | [ ] |
| **P7-5** | [Core] | **Tamper-Resistant Billing Logs**<br>Aggregated logs for auditing. Optional: signed/separate store for Enterprise. | P7-4 | [ ] |

---

## Phase 8: AI Model Training & Deployment
*Goal: Full AI policy lifecycle: train, evaluate, deploy, rollback.*

| ID | Track | Task | Dependencies | Status |
|----|-------|------|--------------|--------|
| **P8-1** | [ML] | **Offline Model Training (GBDT/Linear)**<br>Train admission/TTL model from logged data. Implement Phase A: rules + GBDT. | P4-5 | [ ] |
| **P8-2** | [ML] | **Model Evaluation Pipeline**<br>Offline metrics: expected cost savings, P99 improvement simulation, cache occupancy, eviction count. | P8-1 | [ ] |
| **P8-3** | [ML] | **ONNX Export**<br>Export trained model to ONNX format for Hot Path integration. | P8-1 | [ ] |
| **P8-4** | [API] | **AI Model Management API**<br>`GET /v1/ai/models`, `POST /v1/ai/models/train`, `POST /v1/ai/models/deploy`, `POST /v1/ai/models/rollback`, `GET /v1/ai/evaluations`. | P8-2 | [ ] |
| **P8-5** | [ML] | **Canary Deployment**<br>Deploy model to subset of tenants. Monitor metrics. Auto-rollback on P99 degradation. | P8-4, P4-6 | [ ] |
| **P8-6** | [ML] | **Contextual Bandit (Phase B)**<br>Online fine-tuning of admission/TTL decisions. Explore-exploit for policy optimization. | P8-5 | [ ] |

---

## Phase 9: DR & Enterprise Features
*Goal: Production readiness for Enterprise tier.*

| ID | Track | Task | Dependencies | Status |
|----|-------|------|--------------|--------|
| **P9-1** | [Ops] | **Snapshot & Backup**<br>FAISS index snapshot, vector store snapshot, policy/model metadata backup. | P3-1 | [ ] |
| **P9-2** | [Ops] | **Point-in-Time Recovery**<br>WAL-based recovery. Meet RPO targets (15min Pro, 5min Enterprise). | P9-1 | [ ] |
| **P9-3** | [Ops] | **Alerting System**<br>Implement alerts: P99 breach, hit rate drop, eviction thrashing, FAISS queue depth, memory pressure, AI fallback frequency. | P3-4 | [ ] |
| **P9-4** | [Security] | **Encryption at Rest (KMS)**<br>Encrypt persistent data with external KMS integration. | P5-5 | [ ] |
| **P9-5** | [Security] | **SSO/OIDC Integration**<br>Enterprise authentication via OIDC providers. | P5-5 | [ ] |
| **P9-6** | [ML] | **Per-Tenant Models**<br>Train and deploy tenant-specific admission/TTL models. | P8-5 | [ ] |
| **P9-7** | [Ops] | **Runbook Automation**<br>Implement automated P99 recovery actions: CACHE_HINT=force, param degradation, admission tightening. | P5-4, P9-3 | [ ] |

---

## Phase 10: Future Enhancements (v1.1+)
*Goal: Competitive feature parity and beyond.*

| ID | Track | Task | Dependencies | Status |
|----|-------|------|--------------|--------|
| **P10-1** | [Core] | **Hybrid Search (Dense + Sparse)**<br>Integrate BM25/SPLADE sparse vectors. Implement score fusion with configurable α. | Phase 6 | [ ] |
| **P10-2** | [ML] | **Auto α Tuning**<br>AI-based detection of query type (keyword vs. semantic). Auto-adjust fusion coefficient. | P10-1 | [ ] |
| **P10-3** | [Core] | **Partial Caching (L2/L3)**<br>Route cache (QueryKey→nprobe/efSearch recommendation), Cluster cache (QueryKey→IVF cluster candidates), Meta cache (id→meta snippet). | P6-2 | [ ] |
| **P10-4** | [Core] | **Strong Consistency Mode**<br>Optional strict cache invalidation on writes (Enterprise). | P2-4 | [ ] |
| **P10-5** | [Ops] | **Multi-Region DR**<br>Cross-region replication for disaster recovery. | P9-2 | [ ] |
| **P10-6** | [Core] | **Quota Persistence**<br>Persist tenant quota usage (QPS windows, daily request limits) to survive server restarts. Currently in-memory only. | P5-2 | [ ] |
| **P10-7** | [Security] | **Admin Key Storage (Secret Store)**<br>Support secret stores (Docker secrets, Kubernetes secrets, HashiCorp Vault) or file-based secrets for admin API keys instead of environment variables. | P5-5 | [ ] |
| **P10-8** | [Core] | **HNSW/IVF-PQ Index Implementation**<br>Replace BruteForceVectorIndex with HNSW (graph-based) or IVF-PQ (quantized). Expect 10-100x latency improvement for large datasets. **Note**: Implemented IVF-Flat as first step, achieved ~12x P99 improvement. | P1-1 | [x] |
| **P10-9** | [Core] | **SIMD Vector Distance Optimization**<br>Use `System.Runtime.Intrinsics` for SIMD-accelerated distance calculations (L2, Cosine, IP). Expect 4-8x speedup. | P1-1 | [ ] |
| **P10-10** | [Core] | **Sidecar Communication Optimization**<br>Batch metrics reporting, async fire-and-forget logging, longer policy cache TTL to reduce gRPC overhead. | P4-1 | [ ] |
| **P10-11** | [Core] | **Memory Pool / Object Reuse**<br>Use `ArrayPool<float>` for vector buffers to reduce GC pressure and stabilize latency. | P1-2 | [ ] |

---

## Priority Matrix (Aligned with SPEC.md Section 16)

### Must (MVP)
- P0-1 ~ P0-5: Foundation
- P1-1 ~ P1-6: Basic Vector Operations
- P2-1 ~ P2-5: Result Cache + Epoch Invalidation + LRU
- P3-4: Health & Metrics
- P7-1 ~ P7-3: Basic Metering

### Should (Business Critical)
- P3-1 ~ P3-3: Management APIs
- P5-1 ~ P5-4: Multi-tenancy + QoS + SLO
- P6-2 ~ P6-3: Cost-Aware Semantic Cache
- P6-6: Cost-Aware Query Routing
- P7-4 ~ P7-5: Billing API

### Could (Differentiation)
- P4-*: AI Controller Integration
- P6-4 ~ P6-5: Query Prediction & Prefetch
- P6-8 ~ P6-12: LLM-based Semantic Optimization
- P8-*: AI Model Training Pipeline
- P10-1 ~ P10-2: Hybrid Search
- P9-6: Per-Tenant Models

---

## Current

- Implemented `VEC.ADD`/`VEC.UPSERT` parsing with `VECTOR`, `META`, `TAGS`, and `NUMERIC_FIELDS` handling.
- Added in-memory `VectorStore` and `VectorIndexRegistry` with vector parsing for JSON/CSV/binary payloads.
- Updated command registration and tests to reflect the new write path behavior.
- Implemented `VEC.DEL` with logical deletion, index cleanup, and index epoch tracking.
- Added GitHub Actions CI to run `dotnet test` on PRs and main pushes.
- Implemented `VEC.SEARCH` with brute-force topK search, tag filtering, and optional meta return.
- Added Garnet command tests for `VEC.SEARCH` result ordering, tag filters, and meta output.
- Standardized command responses with `VEC_OK` and error codes for dimension mismatch and missing indexes.
- Implemented `QueryKey` class with custom equality and hashing for vector search caching (P2-1).
- Implemented `ResultCache` (L0) with JSON serialization and epoch-based invalidation (P2-2).
- Implemented `Hot Path Policy Engine` (P2-3) with `IPolicyEngine` interface and `StaticPolicyEngine` (Fixed TTL).
- Integrated `PolicyEngine` and `ResultCache` into `VEC.SEARCH`, enabling cache hits, miss caching, and TTL expiry.
- Added `MemoryCacheStorage` for in-memory caching during search operations.
- automated local quality checks with `scripts/check_quality.sh` and updated `prompt/PROMPT.md` guidelines.
- Implemented **P3-1 Index Management API** using ASP.NET Core hosted within GarnetServer.
- Refactored `GarnetServer` startup to use `IHostedService` for better testability and lifecycle management.
- Added `IndexController` with endpoints for create, build, snapshot, load, and stats.
- Added Integration Tests for Index API using `WebApplicationFactory`.
- Enhanced CI pipeline with format check, code coverage, Python sidecar lint/test, E2E smoke test, and security vulnerability scanning.
- Implemented Tenant Management API with quota create/read/update support.
- Added Cache Management API for policy updates, flush, and invalidate operations backed by an in-memory admin store.
- Added Health and Metrics endpoints returning readiness status and Prometheus-format stats.
- Added TRACE and request_id support to `VEC.SEARCH` with latency breakdown payloads.
- Added sidecar metrics reporting with QPS/miss rate/latency/CPU utilization aggregation and GRPC push to the AI sidecar.
- Implemented AI sidecar feature engineering pipeline for query/system/history signals with history tracking.
- Added unit tests for feature extraction helpers in the AI sidecar.
- Implemented **P4-3 Simple Heuristic Policy (Warm Path)** in Python sidecar with GRPC proto updates and unit tests.
- Implemented **P4-4 Policy Update Mechanism** in Garnet Core (Atomic policy swap & Sidecar integration).
- Implemented **P4-5 Offline Datagen Pipeline** in AI Sidecar (JSONL logging for training).
- Implemented **P4-6 Fallback Guardrail** with warm-path timeout enforcement and `ai_fallback_total` telemetry.
- Added integration test coverage for warm-path timeout fallback and documented `Sidecar:WarmPathTimeoutMs` config.
- Enforced tenant QPS/concurrency limits on vector commands and added per-tenant cache memory caps.
- Added `TenantQuotaEnforcer` and `MemoryCacheStorage` quota tests.
- Implemented **P5-5 Authentication**:
  - HTTP Control Plane (`/v1/*`) requires `X-API-KEY` (admin key).
  - RESP (`VEC.*`) requires `API_KEY <tenantApiKey>` token.
  - Added tenant API key management: `POST /v1/tenants` accepts `apiKey`, plus `PUT /v1/tenants/{tenantId}/apikey`.
- Implemented **P5-4 SLO Guardrails**:
  - `CACHE_HINT=force` cache-only mode (shed cache misses).
  - Automatic degradation when estimated P99 breaches target (BruteForce: `MaxScans` budget via `SearchOptions`).
- Implemented **P5-3 Noisy Neighbor Mitigation**:
  - Added tenant `Priority` (0=high, 1=normal, 2=low).
  - Under degradation: protect high-priority tenants; shed low-priority tenants on cache miss.
- Added **Sidecar gRPC mTLS** (Garnet↔AISidecar): cert-based secure channel + local cert generation script + docker-compose wiring.
- Implemented **P1-6 Vector Benchmarking Data & Tool**: added `src/Pyrope.Benchmarks` (SIFT fvecs / GloVe txt loaders, QPS/latency reporting) and `scripts/bench_vectors.sh`.
- Implemented **P5-6 Authorization (RBAC)**:
  - Added `Role` enum (`Reader`, `Operator`, `TenantAdmin`) and `Permission` enum with role-permission mappings.
  - Added `TenantUser` model and `TenantUserRegistry` for per-user API key management.
  - Added `IAuthorizationService` and `RbacAuthorizationService` for permission checks.
  - Added user management API: `POST /v1/tenants/{id}/users`, `GET /v1/tenants/{id}/users`, `PUT /v1/tenants/{id}/users/{userId}/role`, `DELETE /v1/tenants/{id}/users/{userId}`.
- Implemented **P5-7 Audit Logging**:
  - Added `AuditEvent` model with standard actions (`CREATE_INDEX`, `DELETE_INDEX`, `UPDATE_POLICY`, etc.).
  - Added `IAuditLogger` interface and `AuditLogger` implementation (in-memory + optional JSONL file persistence).
  - Added `AuditController` with `GET /v1/audit/logs` and `GET /v1/audit/stats` endpoints.
  - Integrated audit logging into `IndexController`, `TenantController`, and `CacheController`.
- Implemented **P5-8 Load Testing & Tuning**:
  - Added `scripts/load_test.sh` for running load tests at various concurrency levels with SLO validation.
- Implemented **P10-8 IVF-Flat Index Implementation**:
  - Implemented `IvfFlatVectorIndex` (K-Means Clustering).
  - Integrated into `DeltaVectorIndex` as Tail component.
  - Implemented Compaction logic in `Build()` (Head -> Tail).
  - Updated Benchmarks to support triggered building (`--build-index`).
  - Achieved >8x QPS and >12x Latency P99 improvement.

## Tests

- `dotnet test tests/Pyrope.GarnetServer.Tests/Pyrope.GarnetServer.Tests.csproj`
- `dotnet test Pyrope.sln`
- `./scripts/check_quality.sh`
- `./scripts/check_quality.sh` (P4-1)
- `python3 -m unittest discover -s src/Pyrope.AISidecar/tests -p "test_*.py"`
- `./scripts/check_quality.sh` (P4-3)
- `dotnet test tests/Pyrope.GarnetServer.Tests/Pyrope.GarnetServer.Tests.csproj --filter StaticPolicyEngineTests` (P4-4)
- `PYTHONPATH=src/Pyrope.AISidecar python3 -m unittest discover -s src/Pyrope.AISidecar/tests -p "test_*.py"` (P4-5)
- `./scripts/check_quality.sh` (P4-6)
- `dotnet test tests/Pyrope.GarnetServer.Tests/Pyrope.GarnetServer.Tests.csproj --filter SidecarMetricsReporterTests`
- `dotnet test Pyrope.sln` (P5-2)
- `./scripts/check_quality.sh` (P5-2)
- `src/Pyrope.AISidecar/venv/bin/python -m unittest discover -s src/Pyrope.AISidecar/tests -p "test_*.py"` (P5-5 mTLS)
- `./scripts/generate_mtls_certs.sh` (P5-5 mTLS local certs)
- `dotnet run --project src/Pyrope.Benchmarks --configuration Release -- --dataset synthetic --dim 32 --base-limit 5000 --query-limit 1000 --topk 10 --concurrency 4 --warmup 100 --payload binary --host 127.0.0.1 --port 6380 --tenant tenant_bench --index idx_bench --api-key <tenantApiKey> --http http://127.0.0.1:5000 --admin-api-key <adminApiKey> --cache off --print-stats` (P5-3/4/5)
- `dotnet test Pyrope.sln --configuration Release` (P1-6)
- `./scripts/check_quality.sh` (P1-6)
- `dotnet test tests/Pyrope.GarnetServer.Tests --filter "FullyQualifiedName~Rbac"` (P5-6)
- `dotnet test tests/Pyrope.GarnetServer.Tests --filter "FullyQualifiedName~AuditLogger"` (P5-7)
- `./scripts/load_test.sh` (P5-8)
- `dotnet test tests/Pyrope.GarnetServer.Tests/Pyrope.GarnetServer.Tests.csproj --filter IvfFlatVectorIndexTests` (P10-8)
- `./scripts/bench_vectors.sh ... --build-index` (P10-8 Benchmark)

---

## Changelog

### v1.1 (2024-12-13)
- **Added Phase 3**: Control Plane API (HTTP) - SPEC §5.3 coverage
- **Added Phase 7**: Billing & Metering - SPEC §13 coverage  
- **Added Phase 8**: AI Model Training & Deployment - SPEC §8.3-8.5 coverage
- **Added Phase 9**: DR & Enterprise Features - SPEC §11-12 coverage
- **Added P0-5**: Tenant & Index Data Model - SPEC §4.1-4.2 coverage
- **Added P1-3**: VEC.DEL implementation
- **Added P1-5**: Error Code System - SPEC §5.2 coverage
- **Added P2-4**: Epoch-Based Cache Invalidation
- **Added P4-2**: Feature Engineering Pipeline
- **Added P4-6**: Fallback Guardrail - SPEC §3.1.3 coverage
- **Added P5-3 ~ P5-7**: Noisy Neighbor, Auth, RBAC, Audit - SPEC §9, §11 coverage
- **Added P6-3**: Cost-Aware Semantic Cache - SPEC §17.1 coverage
- **Added P6-6**: Cost-Aware Query Routing - SPEC §17.2 coverage
- **Renamed Phase 4→6**: Advanced Differentiation (expanded)
- **Renamed Phase 5→Phase 5**: Production Hardening (expanded with security)
- **Added Priority Matrix**: Aligned with SPEC §16
