using Microsoft.AspNetCore.Mvc;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Services;

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

            var quotas = request.Quotas ?? new TenantQuota();
            if (_registry.TryCreate(request.TenantId, quotas, out var config))
            {
                return Created($"/v1/tenants/{request.TenantId}", new { config!.TenantId });
            }

            return Conflict("Tenant already exists.");
        }

        [HttpGet("{tenantId}/quotas")]
        public IActionResult GetQuotas(string tenantId)
        {
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

            if (_registry.TryUpdateQuotas(tenantId, request, out var config))
            {
                return Ok(config!.Quotas);
            }

            return NotFound("Tenant not found.");
        }
    }

    public sealed class CreateTenantRequest
    {
        public string TenantId { get; set; } = "";
        public TenantQuota? Quotas { get; set; }
    }
}
