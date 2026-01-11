using Microsoft.AspNetCore.Mvc;
using Pyrope.GarnetServer.Security;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Services;
using Pyrope.GarnetServer.Utils;
using System.Text.Json;

namespace Pyrope.GarnetServer.Controllers
{
    [ApiController]
    [Route("v1/tenants")]
    public class TenantController : ControllerBase
    {
        private readonly TenantRegistry _registry;
        private readonly TenantUserRegistry _userRegistry;
        private readonly IAuditLogger _auditLogger;

        public TenantController(TenantRegistry registry, TenantUserRegistry userRegistry, IAuditLogger auditLogger)
        {
            _registry = registry;
            _userRegistry = userRegistry;
            _auditLogger = auditLogger;
        }

        [HttpPost]
        [RequirePermission(Permission.TenantCreate)]
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
                // Audit log
                _auditLogger.Log(new AuditEvent(
                    action: AuditActions.CreateTenant,
                    resourceType: AuditResourceTypes.Tenant,
                    tenantId: request.TenantId,
                    userId: GetCurrentUserId(),
                    resourceId: request.TenantId,
                    ipAddress: GetClientIp(),
                    success: true
                ));

                return Created($"/v1/tenants/{request.TenantId}", new { config!.TenantId });
            }

            return Conflict("Tenant already exists / API Key already in use.");
        }

        [HttpGet("{tenantId}/quotas")]
        [RequirePermission(Permission.TenantRead)]
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
        [RequirePermission(Permission.TenantUpdate)]
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
                // Audit log
                _auditLogger.Log(new AuditEvent(
                    action: AuditActions.UpdateQuotas,
                    resourceType: AuditResourceTypes.Tenant,
                    tenantId: tenantId,
                    userId: GetCurrentUserId(),
                    resourceId: tenantId,
                    details: JsonSerializer.Serialize(request),
                    ipAddress: GetClientIp(),
                    success: true
                ));

                return Ok(config!.Quotas);
            }

            return NotFound("Tenant not found.");
        }

        [HttpPut("{tenantId}/apikey")]
        [RequirePermission(Permission.TenantUpdate)]
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
                // Audit log
                _auditLogger.Log(new AuditEvent(
                    action: AuditActions.UpdateApiKey,
                    resourceType: AuditResourceTypes.Tenant,
                    tenantId: tenantId,
                    userId: GetCurrentUserId(),
                    resourceId: tenantId,
                    ipAddress: GetClientIp(),
                    success: true
                ));

                return Ok(new { TenantId = tenantId });
            }

            return NotFound("Tenant not found / API Key already in use.");
        }

        // --- RBAC User Management ---

        [HttpPost("{tenantId}/users")]
        [RequirePermission(Permission.UserManage)]
        public IActionResult CreateUser(string tenantId, [FromBody] CreateUserRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.ApiKey))
            {
                return BadRequest("UserId and ApiKey are required.");
            }

            if (!TenantNamespace.TryValidateTenantId(tenantId, out var error))
            {
                return BadRequest(error);
            }

            if (!_registry.TryGet(tenantId, out _))
            {
                return NotFound("Tenant not found.");
            }

            if (!Enum.TryParse<Role>(request.Role, true, out var role))
            {
                return BadRequest($"Invalid role. Allowed values: {string.Join(", ", Enum.GetNames<Role>())}");
            }

            if (_userRegistry.TryCreate(tenantId, request.UserId, role, request.ApiKey, out var user))
            {
                // Audit log
                _auditLogger.Log(new AuditEvent(
                    action: AuditActions.CreateUser,
                    resourceType: AuditResourceTypes.User,
                    tenantId: tenantId,
                    userId: GetCurrentUserId(),
                    resourceId: request.UserId,
                    details: JsonSerializer.Serialize(new { request.UserId, Role = role.ToString() }),
                    ipAddress: GetClientIp(),
                    success: true
                ));

                return Created($"/v1/tenants/{tenantId}/users/{request.UserId}", new
                {
                    user!.UserId,
                    user.TenantId,
                    Role = user.Role.ToString()
                });
            }

            return Conflict("User already exists / API Key already in use.");
        }

        [HttpGet("{tenantId}/users")]
        [RequirePermission(Permission.UserManage)]
        public IActionResult GetUsers(string tenantId)
        {
            if (!TenantNamespace.TryValidateTenantId(tenantId, out var error))
            {
                return BadRequest(error);
            }

            if (!_registry.TryGet(tenantId, out _))
            {
                return NotFound("Tenant not found.");
            }

            var users = _userRegistry.GetByTenant(tenantId).Select(u => new
            {
                u.UserId,
                u.TenantId,
                Role = u.Role.ToString(),
                CreatedAt = u.CreatedAt.ToString("o"),
                UpdatedAt = u.UpdatedAt.ToString("o")
            });

            return Ok(users);
        }

        [HttpPut("{tenantId}/users/{userId}/role")]
        [RequirePermission(Permission.UserManage)]
        public IActionResult UpdateUserRole(string tenantId, string userId, [FromBody] UpdateUserRoleRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Role))
            {
                return BadRequest("Role is required.");
            }

            if (!TenantNamespace.TryValidateTenantId(tenantId, out var error))
            {
                return BadRequest(error);
            }

            if (!Enum.TryParse<Role>(request.Role, true, out var role))
            {
                return BadRequest($"Invalid role. Allowed values: {string.Join(", ", Enum.GetNames<Role>())}");
            }

            if (_userRegistry.TryUpdateRole(tenantId, userId, role, out var user))
            {
                // Audit log
                _auditLogger.Log(new AuditEvent(
                    action: AuditActions.UpdateUserRole,
                    resourceType: AuditResourceTypes.User,
                    tenantId: tenantId,
                    userId: GetCurrentUserId(),
                    resourceId: userId,
                    details: JsonSerializer.Serialize(new { UserId = userId, Role = role.ToString() }),
                    ipAddress: GetClientIp(),
                    success: true
                ));

                return Ok(new
                {
                    user!.UserId,
                    user.TenantId,
                    Role = user.Role.ToString()
                });
            }

            return NotFound("User not found.");
        }

        [HttpDelete("{tenantId}/users/{userId}")]
        [RequirePermission(Permission.UserManage)]
        public IActionResult DeleteUser(string tenantId, string userId)
        {
            if (!TenantNamespace.TryValidateTenantId(tenantId, out var error))
            {
                return BadRequest(error);
            }

            if (_userRegistry.TryDelete(tenantId, userId, out _))
            {
                // Audit log
                _auditLogger.Log(new AuditEvent(
                    action: AuditActions.DeleteUser,
                    resourceType: AuditResourceTypes.User,
                    tenantId: tenantId,
                    userId: GetCurrentUserId(),
                    resourceId: userId,
                    ipAddress: GetClientIp(),
                    success: true
                ));

                return Ok(new { Message = "User deleted." });
            }

            return NotFound("User not found.");
        }

        private string? GetCurrentUserId()
        {
            return HttpContext?.Items["PyropeUserId"]?.ToString();
        }

        private string? GetClientIp()
        {
            return HttpContext?.Connection?.RemoteIpAddress?.ToString();
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

    public sealed class CreateUserRequest
    {
        public string UserId { get; set; } = "";
        public string Role { get; set; } = "Reader";
        public string ApiKey { get; set; } = "";
    }

    public sealed class UpdateUserRoleRequest
    {
        public string Role { get; set; } = "";
    }
}
