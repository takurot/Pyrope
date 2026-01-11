using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Pyrope.GarnetServer.Security;
using Pyrope.GarnetServer.Services;

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
        public IActionResult GetStats()
        {
            return Ok(new
            {
                TotalEvents = _auditLogger.Count
            });
        }

        private string? GetCurrentUserId() => HttpContext?.Items["PyropeUserId"]?.ToString();
    }
}
