using Microsoft.AspNetCore.Mvc;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Services;
using Pyrope.GarnetServer.Utils;

namespace Pyrope.GarnetServer.Controllers
{
    [ApiController]
    [Route("v1/tenants")]
    public class TenantController : ControllerBase
    {
        private readonly TenantRegistry _registry;

        public TenantController(TenantRegistry registry)
        {
            _registry = registry;
        }

        [HttpPost]
        public IActionResult CreateTenant([FromBody] CreateTenantRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.TenantId))
            {
                return BadRequest("Invalid request.");
            }

            if (!TenantNamespace.TryValidateTenantId(request.TenantId, out var error))
            {
                return BadRequest(error);
            }

            var quotas = request.Quotas ?? new TenantQuota();
            if (_registry.TryCreate(request.TenantId, quotas, out var config, apiKey: request.ApiKey))
            {
                return Created($"/v1/tenants/{request.TenantId}", new { config!.TenantId });
            }

            return Conflict("Tenant already exists.");
        }

        [HttpGet("{tenantId}/quotas")]
        public IActionResult GetQuotas(string tenantId)
        {
            if (!TenantNamespace.TryValidateTenantId(tenantId, out var error))
            {
                return BadRequest(error);
            }

            if (_registry.TryGet(tenantId, out var config))
            {
                return Ok(config!.Quotas);
            }

            return NotFound("Tenant not found.");
        }

        [HttpPut("{tenantId}/quotas")]
        public IActionResult UpdateQuotas(string tenantId, [FromBody] TenantQuota request)
        {
            if (request == null)
            {
                return BadRequest("Invalid request.");
            }

            if (!TenantNamespace.TryValidateTenantId(tenantId, out var error))
            {
                return BadRequest(error);
            }

            if (_registry.TryUpdateQuotas(tenantId, request, out var config))
            {
                return Ok(config!.Quotas);
            }

            return NotFound("Tenant not found.");
        }

        [HttpPut("{tenantId}/apikey")]
        public IActionResult UpdateApiKey(string tenantId, [FromBody] UpdateTenantApiKeyRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ApiKey))
            {
                return BadRequest("ApiKey is required.");
            }

            if (!TenantNamespace.TryValidateTenantId(tenantId, out var error))
            {
                return BadRequest(error);
            }

            if (_registry.TryUpdateApiKey(tenantId, request.ApiKey, out _))
            {
                return Ok(new { TenantId = tenantId });
            }

            return NotFound("Tenant not found.");
        }
    }

    public sealed class CreateTenantRequest
    {
        public string TenantId { get; set; } = "";
        public TenantQuota? Quotas { get; set; }
        public string? ApiKey { get; set; }
    }

    public sealed class UpdateTenantApiKeyRequest
    {
        public string ApiKey { get; set; } = "";
    }
}
