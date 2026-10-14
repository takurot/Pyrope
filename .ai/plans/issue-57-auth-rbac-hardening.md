# Implementation Plan - Issue #57: Auth/RBAC subsystem fail-open paths and bypasses (Revision 2)

## 1. Background & Problem Analysis
Issue #57 identifies systemic fail-open paths and security bypasses in the Auth/RBAC subsystem:
1. `RequirePermissionAttribute.cs`: Silently falls back to literal `"system"` tenant when no tenantId is found (`tenantId ?? "system"`). The body-extraction block is an unimplemented stub.
2. `TenantApiKeyAuthenticator.cs`: Returns `true` for every request when `_options.Enabled` is `false`, failing open and bypassing authentication entirely.
3. `SessionAuthContext.cs`: Stores authenticated tenant identity in static `AsyncLocal<string?>` without validation on `Set()` and without reset on connection close / failed auth, risking tenant identity leaks across execution contexts and thread pool re-use.
4. `RbacAuthorizationService.cs` (and others): Uses non-constant-time `string.Equals` for API key comparison, exposing a timing side-channel.

## 2. Detailed Technical Design & Changes

### 2.1 Constant-Time String Comparison
- Create `SecurityUtils.FixedTimeEquals(string? a, string? b)`:
  - If either string is null, returns false.
  - Converts strings to UTF-8 bytes and performs constant-time comparison using `System.Security.Cryptography.CryptographicOperations.FixedTimeEquals`.
- Replace `string.Equals` with `SecurityUtils.FixedTimeEquals` in:
  - `TenantApiKeyAuthenticator.cs`
  - `RbacAuthorizationService.cs` (`GetRole`, `GetUserId`)
  - `ApiKeyAuthMiddleware.cs` (`AdminApiKey` check)

### 2.2 `RequirePermissionAttribute.cs` Hardening
- Eliminate silent `"system"` tenant fallback completely.
- Extract `tenantId` strictly from the requested resource:
  1. RouteData: `context.RouteData.Values["tenantId"]?.ToString()` or `context.RouteData.Values["id"]?.ToString()`
  2. Query string: `context.HttpContext.Request.Query["tenantId"].ToString()`
  3. ActionArguments: inspect argument objects for a `TenantId` property (e.g. `CreateIndexRequest.TenantId`, `CreateTenantRequest.TenantId`)
- For non-global-admin callers:
  - **Fail Closed**: If `string.IsNullOrWhiteSpace(tenantId)`, immediately return `context.Result = new ForbidResult();`.
  - Validate format: Call `TenantNamespace.TryValidateTenantId(tenantId, out _)`. If invalid, return `ForbidResult()`.
  - **Cross-Tenant Guard**: If `context.HttpContext.Items["PyropeTenantId"]` exists (set by middleware for tenant/user keys), verify that `string.Equals(tenantId, contextTenantId, StringComparison.Ordinal)`. If they do not match, return `ForbidResult()`.
  - Check permission: Call `authService.HasPermission(tenantId, apiKey, _permission)`. If false, return `ForbidResult()`.
  - Record `PyropeUserId` and `PyropeUserRole` into `context.HttpContext.Items`.

### 2.3 `TenantApiKeyAuthenticator.cs` Hardening
- Remove fail-open bypass `if (!_options.Enabled) return true;`.
- Enforce strict validation:
  - Validate `tenantId` is not null/empty/whitespace and passes `TenantNamespace.TryValidateTenantId`.
  - Check tenant exists in `_registry`.
  - Check tenant has configured API key.
  - Compare API keys using `SecurityUtils.FixedTimeEquals`.
  - Return `false` with descriptive `errorMessage` upon any check failure.

### 2.4 `SessionAuthContext.cs` & `PyropeGarnetAuthenticator.cs` Hardening
- `SessionAuthContext.Set(string tenantId)`:
  - Validate non-null, non-whitespace, and valid format via `TenantNamespace.TryValidateTenantId`.
  - Throw `ArgumentException` on invalid tenantId.
- `SessionAuthContext.CreateScope(string tenantId)`:
  - Captures `_previous = _authenticatedTenantId.Value`.
  - Calls `Set(tenantId)`.
  - Returns an `IDisposable` that restores `_authenticatedTenantId.Value = _previous` on `Dispose()` to support nested execution safely.
- `PyropeGarnetAuthenticator.cs`:
  - In constructor: explicitly call `SessionAuthContext.Reset()` to guard against recycled worker threads.
  - Implement `IDisposable`: calls `SessionAuthContext.Reset()` on connection disposal.
  - In `Authenticate`: on any failure branch (unknown tenant, bad key, malformed credentials), explicitly call `SessionAuthContext.Reset()`.

### 2.5 CI & Smoke Test Updates (`tests/smoke_test.py`, `.github/workflows/ci.yml`)
- Update `tests/smoke_test.py`:
  - Add optional `--http-port` (default 5000) and `--admin-api-key` (default `dev`).
  - Prior to issuing vector commands, provision `smoke_tenant` with API key `smoke_secret` via `POST /v1/tenants`.
  - Execute Redis `AUTH smoke_tenant smoke_secret` before running vector commands.
- Update `.github/workflows/ci.yml`:
  - Set `PYROPE_ADMIN_API_KEY: "dev"` in `Start Server` step.
  - Remove `Auth__Enabled: "false"`.
  - Run `python tests/smoke_test.py --port 3278 --http-port 5000 --admin-api-key dev`.

## 3. Test Strategy (TDD)
1. `RequirePermissionAttributeTests`:
   - Non-admin with missing `tenantId` on global endpoint or queryless endpoint returns 403 Forbidden.
   - Non-admin requesting another tenant's `tenantId` returns 403 Forbidden.
   - Request with model in body/ActionArguments extracts `tenantId` and authorizes correctly.
2. `TenantApiKeyAuthenticatorTests`:
   - `TryAuthenticate` returns false when tenant doesn't exist, has no key, or key mismatch.
   - Constant-time comparison verified.
3. `SessionAuthContextTests`:
   - `Set(null)` and `Set("invalid..tenant")` throw `ArgumentException`.
   - `CreateScope` safely restores previous outer context when nested scopes exit.
   - `PyropeGarnetAuthenticator` cleans context at start, on bad auth, and on dispose.
4. `RbacAuthorizationServiceTests`:
   - Verify constant-time comparison in `GetRole` and `GetUserId`.
5. Full Regression & Smoke Test:
   - Run `./scripts/check_quality.sh`.
   - Run updated `tests/smoke_test.py` against live server.

## 4. Acceptance Criteria
- Full test pass via `dotnet test Pyrope.sln`.
- `./scripts/check_quality.sh` passes 100%.
- E2E smoke test passes with real AUTH and tenant provisioning.
