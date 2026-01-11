using System;
using Pyrope.GarnetServer.Security;

namespace Pyrope.GarnetServer.Model
{
    /// <summary>
    /// Represents a user within a tenant with RBAC role assignment.
    /// </summary>
    public sealed class TenantUser
    {
        public string UserId { get; }
        public string TenantId { get; }
        public Role Role { get; private set; }
        public string ApiKey { get; private set; }
        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset UpdatedAt { get; private set; }

        public TenantUser(string userId, string tenantId, Role role, string apiKey, DateTimeOffset createdAt)
        {
            if (string.IsNullOrWhiteSpace(userId))
                throw new ArgumentException("User ID cannot be empty.", nameof(userId));
            if (string.IsNullOrWhiteSpace(tenantId))
                throw new ArgumentException("Tenant ID cannot be empty.", nameof(tenantId));
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("API key cannot be empty.", nameof(apiKey));

            UserId = userId;
            TenantId = tenantId;
            Role = role;
            ApiKey = apiKey;
            CreatedAt = createdAt;
            UpdatedAt = createdAt;
        }

        public void UpdateRole(Role role, DateTimeOffset updatedAt)
        {
            Role = role;
            UpdatedAt = updatedAt;
        }

        public void UpdateApiKey(string apiKey, DateTimeOffset updatedAt)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("API key cannot be empty.", nameof(apiKey));
            ApiKey = apiKey;
            UpdatedAt = updatedAt;
        }
    }
}
