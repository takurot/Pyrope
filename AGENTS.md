# Repository Guidelines

## Project Structure & Module Organization
- `src/Pyrope.GarnetServer` – .NET 10 RESP/HTTP server (core product).
- `src/Pyrope.Benchmarks` – C# benchmarking CLI used by `scripts/bench_vectors.sh`.
- `src/Pyrope.AISidecar` – Python gRPC sidecar (policy/metrics). Uses `Protos/`.
- `src/Protos` – gRPC proto definitions (policy_service.proto).
- `tests/Pyrope.GarnetServer.Tests` – xUnit tests for server/benchmarks.
- `tests/smoke_test.py` – Redis client smoke test; `tests/requirements.txt` for deps.
- `scripts/` – `check_quality.sh`, `bench_vectors.sh`, `generate_mtls_certs.sh`.

## Build, Test, and Development Commands
- Build: `dotnet build Pyrope.sln`
- Test (C#): `dotnet test Pyrope.sln` (coverage: `--collect:"XPlat Code Coverage"`).
- Quality checks: `./scripts/check_quality.sh` (format, build, test, Python lint).
- Run server (local):
  ```bash
  PYROPE_ADMIN_API_KEY=dev dotnet run --project src/Pyrope.GarnetServer -- --port 3278 --bind 127.0.0.1
  ```
- Run benchmarks: `./scripts/bench_vectors.sh --dataset synthetic --dim 128 --topk 10`.
- Sidecar (Python):
  ```bash
  cd src/Pyrope.AISidecar && python -m venv .venv && source .venv/bin/activate
  pip install -r requirements.txt && python codegen.py && python server.py
  ```
- Docker (mTLS demo): `docker-compose up --build` (generate certs: `./scripts/generate_mtls_certs.sh`).

## Coding Style & Naming Conventions
- C#: enforce with `dotnet format`; 4‑space indent; files match type names; `PascalCase` types/methods, `camelCase` locals.
- Python: `black --line-length 120` and `flake8` (E501/W503 ignored by script); 4‑space indent; modules `snake_case.py`.
- Protos: keep service and message names `PascalCase`; prefer backward‑compatible changes.

## Testing Guidelines
- C#: xUnit with `*Tests.cs` (e.g., `VectorStoreTests.cs`); run `dotnet test` from repo root.
- Python (sidecar): unittest under `src/Pyrope.AISidecar/tests`; run `python -m unittest discover -s src/Pyrope.AISidecar/tests`.
- Smoke test: `pip install -r tests/requirements.txt && python tests/smoke_test.py` (requires server running on 3278).

## Commit & Pull Request Guidelines
- Commit style: conventional prefixes (`feat:`, `fix:`, `docs:`, `test:`, `chore:`). Use imperative, concise subjects; add scope when helpful.
- Before pushing: run `./scripts/check_quality.sh`. If `Protos/` changed, run `src/Pyrope.AISidecar/codegen.py`. Do not commit generated gRPC stubs.
- PRs: include a clear description, linked issues, test coverage or smoke output, and any config/env changes (e.g., `PYROPE_ADMIN_API_KEY`).

## Security & Configuration Tips
- Never commit secrets. For local dev, use `generate_mtls_certs.sh`; configure mTLS env vars as in `docker-compose.yml`.
- Default logs: sidecar writes to `logs/query_log.jsonl`.
