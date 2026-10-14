using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Pyrope.GarnetServer.Security;
using Pyrope.GarnetServer.Services;
using Pyrope.GarnetServer.Utils;

namespace Pyrope.GarnetServer.Controllers
{
    [ApiController]
    [Route("v1/audit")]
    public class AuditController : ControllerBase
    {
        private readonly IAuditLogger _auditLogger;

        public AuditController(IAuditLogger auditLogger)
        {
            _auditLogger = auditLogger ?? throw new ArgumentNullException(nameof(auditLogger));
        }

        /// <summary>
        /// Query audit logs with optional filters.
        /// </summary>
        [HttpGet("logs")]
        [RequirePermission(Permission.AuditRead)]
        public IActionResult GetLogs(
            [FromQuery] string? tenantId = null,
            [FromQuery] string? from = null,
            [FromQuery] string? to = null,
            [FromQuery] string? action = null,
            [FromQuery] int limit = 100)
        {
            if (!string.IsNullOrWhiteSpace(tenantId) && !TenantNamespace.TryValidateTenantId(tenantId, out var error))
            {
                return BadRequest(error);
            }

            var isAdmin = IsAdmin();
            var contextTenantId = GetCallerTenantId();

            if (!isAdmin)
            {
                if (string.IsNullOrWhiteSpace(tenantId))
                {
                    tenantId = contextTenantId;
                }

                if (string.IsNullOrWhiteSpace(tenantId) ||
                    (!string.IsNullOrWhiteSpace(contextTenantId) && !string.Equals(tenantId, contextTenantId, StringComparison.Ordinal)))
                {
                    return Forbid();
                }
            }

            if (limit < 1) limit = 1;
            if (limit > 1000) limit = 1000;

            DateTimeOffset? fromDate = null;
            DateTimeOffset? toDate = null;

            if (!string.IsNullOrWhiteSpace(from))
            {
                if (!DateTimeOffset.TryParse(from, out var parsed))
                {
                    return BadRequest("Invalid 'from' date format. Use ISO 8601.");
                }
                fromDate = parsed;
            }

            if (!string.IsNullOrWhiteSpace(to))
            {
                if (!DateTimeOffset.TryParse(to, out var parsed))
                {
                    return BadRequest("Invalid 'to' date format. Use ISO 8601.");
                }
                toDate = parsed;
            }

            var events = _auditLogger.Query(tenantId, fromDate, toDate, action, limit);

            return Ok(new
            {
                Count = events.Count(),
                Events = events.Select(e => new
                {
                    e.EventId,
                    Timestamp = e.Timestamp.ToString("o"),
                    e.TenantId,
                    e.UserId,
                    e.Action,
                    e.ResourceType,
                    e.ResourceId,
                    e.Details,
                    e.Success
                })
            });
        }

        /// <summary>
        /// Get audit log count.
        /// </summary>
        [HttpGet("stats")]
        [RequirePermission(Permission.AuditRead)]
        public IActionResult GetStats([FromQuery] string? tenantId = null)
        {
            if (!string.IsNullOrWhiteSpace(tenantId) && !TenantNamespace.TryValidateTenantId(tenantId, out var error))
            {
                return BadRequest(error);
            }

            var isAdmin = IsAdmin();
            var contextTenantId = GetCallerTenantId();

            if (!isAdmin)
            {
                if (string.IsNullOrWhiteSpace(tenantId))
                {
                    tenantId = contextTenantId;
                }

                if (string.IsNullOrWhiteSpace(tenantId) ||
                    (!string.IsNullOrWhiteSpace(contextTenantId) && !string.Equals(tenantId, contextTenantId, StringComparison.Ordinal)))
                {
                    return Forbid();
                }
            }

            return Ok(new
            {
                TotalEvents = _auditLogger.GetCount(tenantId)
            });
        }

        private bool IsAdmin() =>
            HttpContext?.Items.TryGetValue("IsAdmin", out var obj) == true && obj is bool admin && admin;

        private string? GetCallerTenantId() =>
            HttpContext?.Items["PyropeTenantId"]?.ToString();
    }
}
