using Microsoft.AspNetCore.Mvc;
using Pyrope.GarnetServer.Security;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Policies;
using Pyrope.GarnetServer.Services;
using Pyrope.GarnetServer.Utils;
using System.Text.Json;

namespace Pyrope.GarnetServer.Controllers
{
    [ApiController]
    [Route("v1/cache")]
    public class CacheController : ControllerBase
    {
        private readonly CachePolicyStore _policyStore;
        private readonly ICacheAdmin _cacheAdmin;
        private readonly IAuditLogger _auditLogger;

        public CacheController(CachePolicyStore policyStore, ICacheAdmin cacheAdmin, IAuditLogger auditLogger)
        {
            _policyStore = policyStore;
            _cacheAdmin = cacheAdmin;
            _auditLogger = auditLogger;
        }

        [HttpGet("policies")]
        [RequirePermission(Permission.PolicyRead)]
        public IActionResult GetPolicies()
        {
            return Ok(_policyStore.Current);
        }

        [HttpPut("policies")]
        [RequirePermission(Permission.PolicyUpdate)]
        public IActionResult UpdatePolicies([FromBody] CachePolicyConfig request)
        {
            if (request == null)
            {
                return BadRequest("Invalid request.");
            }

            if (request.DefaultTtlSeconds < 0)
            {
                return BadRequest("DefaultTtlSeconds must be >= 0.");
            }

            _policyStore.Update(request);

            // Audit log
            _auditLogger.Log(new AuditEvent(
                action: AuditActions.UpdatePolicy,
                resourceType: AuditResourceTypes.Policy,
                userId: GetCurrentUserId(),
                details: JsonSerializer.Serialize(new { request.DefaultTtlSeconds, request.EnableCache }),
                ipAddress: GetClientIp(),
                success: true
            ));

            return Ok(request);
        }

        [HttpPost("flush")]
        [RequirePermission(Permission.CacheFlush)]
        public IActionResult Flush()
        {
            var removed = _cacheAdmin.Clear();

            // Audit log
            _auditLogger.Log(new AuditEvent(
                action: AuditActions.FlushCache,
                resourceType: AuditResourceTypes.Cache,
                userId: GetCurrentUserId(),
                details: JsonSerializer.Serialize(new { RemovedCount = removed }),
                ipAddress: GetClientIp(),
                success: true
            ));

            return Ok(new { Removed = removed });
        }

        [HttpPost("invalidate")]
        [RequirePermission(Permission.CacheInvalidate)]
        public IActionResult Invalidate([FromBody] CacheInvalidateRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.TenantId) || string.IsNullOrWhiteSpace(request.IndexName))
            {
                return BadRequest("TenantId and IndexName are required.");
            }

            if (!TenantNamespace.TryValidateTenantId(request.TenantId, out var tenantError))
            {
                return BadRequest(tenantError);
            }

            if (!TenantNamespace.TryValidateIndexName(request.IndexName, out var indexError))
            {
                return BadRequest(indexError);
            }

            var prefix = KeyUtils.GetCacheKeyPrefix(request.TenantId, request.IndexName);
            var removed = _cacheAdmin.RemoveByPrefix(prefix);

            // Audit log
            _auditLogger.Log(new AuditEvent(
                action: AuditActions.InvalidateCache,
                resourceType: AuditResourceTypes.Cache,
                tenantId: request.TenantId,
                userId: GetCurrentUserId(),
                resourceId: request.IndexName,
                details: JsonSerializer.Serialize(new { RemovedCount = removed }),
                ipAddress: GetClientIp(),
                success: true
            ));

            return Ok(new { Removed = removed });
        }

        private string? GetCurrentUserId() => HttpContext?.Items["PyropeUserId"]?.ToString();

        private string? GetClientIp() => HttpContext?.Connection?.RemoteIpAddress?.ToString();
    }

    public sealed class CacheInvalidateRequest
    {
        public string TenantId { get; set; } = "";
        public string IndexName { get; set; } = "";
    }
}
