using System;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Services;

namespace Pyrope.GarnetServer.Security
{
    /// <summary>
    /// RBAC-based authorization service.
    /// Supports both legacy tenant-level API keys and per-user API keys.
    /// </summary>
    public sealed class RbacAuthorizationService : IAuthorizationService
    {
        private readonly TenantUserRegistry _userRegistry;
        private readonly TenantRegistry _tenantRegistry;

        public RbacAuthorizationService(TenantUserRegistry userRegistry, TenantRegistry tenantRegistry)
        {
            _userRegistry = userRegistry ?? throw new ArgumentNullException(nameof(userRegistry));
            _tenantRegistry = tenantRegistry ?? throw new ArgumentNullException(nameof(tenantRegistry));
        }

        /// <inheritdoc/>
        public bool HasPermission(string tenantId, string apiKey, Permission permission)
        {
            var role = GetRole(tenantId, apiKey);
            if (role == null)
                return false;

            return RolePermissions.HasPermission(role.Value, permission);
        }

        /// <inheritdoc/>
        public Role? GetRole(string tenantId, string apiKey)
        {
            if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(apiKey))
                return null;

            // First, try to find a user with this API key
            if (_userRegistry.TryGetByApiKey(apiKey, out var user) && user != null)
            {
                // Ensure the user belongs to the requested tenant
                if (string.Equals(user.TenantId, tenantId, StringComparison.Ordinal))
                {
                    return user.Role;
                }
                return null;
            }

            // Fallback: check legacy tenant-level API key (treated as TenantAdmin for backward compatibility)
            if (_tenantRegistry.TryGet(tenantId, out var tenantConfig) && tenantConfig != null)
            {
                if (string.Equals(tenantConfig.ApiKey, apiKey, StringComparison.Ordinal))
                {
                    return Role.TenantAdmin;
                }
            }

            return null;
        }

        /// <inheritdoc/>
        public string? GetUserId(string tenantId, string apiKey)
        {
            if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(apiKey))
                return null;

            // Check user registry
            if (_userRegistry.TryGetByApiKey(apiKey, out var user) && user != null)
            {
                if (string.Equals(user.TenantId, tenantId, StringComparison.Ordinal))
                {
                    return user.UserId;
                }
                return null;
            }

            // Fallback: legacy tenant API key is treated as "admin" user
            if (_tenantRegistry.TryGet(tenantId, out var tenantConfig) && tenantConfig != null)
            {
                if (string.Equals(tenantConfig.ApiKey, apiKey, StringComparison.Ordinal))
                {
                    return "admin";
                }
            }

            return null;
        }
    }
}
