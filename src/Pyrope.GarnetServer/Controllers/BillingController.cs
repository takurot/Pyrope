using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Security;
using Pyrope.GarnetServer.Services;
using Pyrope.GarnetServer.Utils;

namespace Pyrope.GarnetServer.Controllers
{
    [ApiController]
    [Route("v1/billing")]
    public class BillingController : ControllerBase
    {
        private readonly IBillingMeter _billingMeter;
        private readonly ICacheUsageProvider _cacheUsageProvider;
        private readonly TenantRegistry _tenantRegistry;

        public BillingController(IBillingMeter billingMeter, ICacheUsageProvider cacheUsageProvider, TenantRegistry tenantRegistry)
        {
            _billingMeter = billingMeter ?? throw new ArgumentNullException(nameof(billingMeter));
            _cacheUsageProvider = cacheUsageProvider ?? throw new ArgumentNullException(nameof(cacheUsageProvider));
            _tenantRegistry = tenantRegistry ?? throw new ArgumentNullException(nameof(tenantRegistry));
        }

        [HttpGet("usage")]
        [RequirePermission(Permission.BillingRead)]
        public IActionResult GetUsage([FromQuery] string? tenantId = null)
        {
            var isAdmin = HttpContext.Items.TryGetValue("IsAdmin", out var isAdminObj) && isAdminObj is bool admin && admin;
            var contextTenantId = HttpContext.Items["PyropeTenantId"]?.ToString();

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

            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                if (!TenantNamespace.TryValidateTenantId(tenantId, out var tenantError))
                {
                    return BadRequest(tenantError);
                }

                var tenantExists = _tenantRegistry.TryGet(tenantId, out _);
                if (!_billingMeter.TryGetUsage(tenantId, out var usage))
                {
                    if (!tenantExists)
                    {
                        return NotFound("Tenant not found.");
                    }

                    usage = new TenantBillingUsage(
                        tenantId,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        DateTimeOffset.UtcNow);
                }

                var cacheBytes = _cacheUsageProvider.GetTenantUsageBytes(tenantId);
                return Ok(ToResponse(usage, cacheBytes));
            }

            var usageList = _billingMeter.GetAllUsage();
            var cacheUsage = _cacheUsageProvider.GetAllTenantUsageBytes();

            var tenants = usageList
                .Select(u => ToResponse(u, cacheUsage.TryGetValue(u.TenantId, out var bytes) ? bytes : 0))
                .ToList();

            return Ok(new
            {
                Count = tenants.Count,
                Tenants = tenants
            });
        }

        private static object ToResponse(TenantBillingUsage usage, long cacheBytes)
        {
            return new
            {
                usage.TenantId,
                Requests = new
                {
                    usage.RequestsTotal,
                    usage.CacheHits,
                    usage.CacheMisses
                },
                Compute = new
                {
                    usage.ComputeCostUnits,
                    usage.ComputeSeconds
                },
                Storage = new
                {
                    usage.VectorStorageBytes,
                    usage.SnapshotStorageBytes
                },
                Cache = new
                {
                    CacheMemoryBytes = cacheBytes
                },
                UpdatedAt = usage.UpdatedAt.ToString("o")
            };
        }
    }
}
