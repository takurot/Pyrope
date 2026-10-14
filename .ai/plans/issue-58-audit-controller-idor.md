# Implementation Plan - Issue #58: IDOR Cross-tenant audit log exposure in AuditController (Revision 2)

## 1. Background & Problem Analysis
Issue #58 identifies an Insecure Direct Object Reference (IDOR) vulnerability in `AuditController.cs`:
- `GetLogs`: The `tenantId` filter is accepted directly from the query string. Any caller with `AuditRead` permission can pass `?tenantId=OtherTenant` and read arbitrary tenants' audit logs, or omit `tenantId` to query global audit logs.
- `GetStats`: Returns the global audit event count across all tenants (`_auditLogger.Count`) without tenant scoping.
- Dead code: `GetCurrentUserId()` is unused in `AuditController.cs`.

## 2. Technical Design & Changes

### 2.1 `IAuditLogger` Enhancement
In `src/Pyrope.GarnetServer/Services/IAuditLogger.cs` and `AuditLogger.cs`:
- Add `int GetCount(string? tenantId = null);`
  - When `tenantId` is null/empty: returns `Count` (all events).
  - When `tenantId` is provided: returns count of events where `e.TenantId == tenantId` using ordinal string comparison.
  - This avoids `limit` truncation and unnecessary event list allocation.

### 2.2 `AuditController.cs` Authorization Hardening
In `src/Pyrope.GarnetServer/Controllers/AuditController.cs`:
1. `GetLogs`:
   - Inspect `HttpContext.Items["IsAdmin"]` and `HttpContext.Items["PyropeTenantId"]`.
   - If caller is **not** a global admin:
     - If `tenantId` is omitted, default to caller's authenticated tenant (`contextTenantId`).
     - If `tenantId` does not match `contextTenantId`, return `Forbid()`.
   - If `tenantId` is provided (by admin or non-admin):
     - Validate with `TenantNamespace.TryValidateTenantId(tenantId, out var error)`. If invalid, return `BadRequest(error)`.
   - Execute query with validated `tenantId`.
2. `GetStats`:
   - Accept optional `[FromQuery] string? tenantId = null`.
   - If caller is **not** a global admin:
     - If `tenantId` is omitted, default to `contextTenantId`.
     - If `tenantId` does not match `contextTenantId`, return `Forbid()`.
   - If `tenantId` is provided:
     - Validate with `TenantNamespace.TryValidateTenantId(tenantId, out var error)`. If invalid, return `BadRequest(error)`.
   - Return `Ok(new { TotalEvents = _auditLogger.GetCount(tenantId) })` preserving backward compatibility of the response JSON schema (`TotalEvents`).
3. Dead Code Removal:
   - Remove unused private method `GetCurrentUserId()`.

## 3. Test Strategy (TDD)
Create `tests/Pyrope.GarnetServer.Tests/Controllers/AuditControllerTests.cs`:
1. `GetLogs_NonAdmin_DifferentTenant_ReturnsForbid`: Caller authenticated as `tenant1` querying `?tenantId=tenant2` returns 403 Forbidden.
2. `GetLogs_NonAdmin_NoTenantParam_DefaultsToOwnTenant`: Caller authenticated as `tenant1` querying without `tenantId` returns 200 OK with only `tenant1` events.
3. `GetLogs_NonAdmin_OwnTenant_ReturnsOk`: Caller authenticated as `tenant1` querying `?tenantId=tenant1` returns 200 OK.
4. `GetLogs_InvalidTenantFormat_ReturnsBadRequest`: Querying with invalid tenantId format returns 400 Bad Request.
5. `GetStats_NonAdmin_DifferentTenant_ReturnsForbid`: Non-admin requesting stats for another tenant returns 403.
6. `GetStats_NonAdmin_DefaultsToOwnTenant`: Non-admin requesting stats without tenantId returns only own tenant's count with property name `TotalEvents`.
7. `GetLogs_Admin_CanQueryAnyOrAllTenants`: Admin can query without tenantId (global logs) or any specific tenantId.
8. `GetStats_Admin_GlobalAndScoped`: Admin can get global stats or tenant-scoped stats.

## 4. Acceptance Criteria
- Unit tests pass.
- `./scripts/check_quality.sh` passes 100%.
