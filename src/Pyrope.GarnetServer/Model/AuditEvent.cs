using System;

namespace Pyrope.GarnetServer.Model
{
    /// <summary>
    /// Represents an audit log entry for management operations.
    /// </summary>
    public sealed class AuditEvent
    {
        /// <summary>Unique event identifier.</summary>
        public string EventId { get; }

        /// <summary>When the event occurred.</summary>
        public DateTimeOffset Timestamp { get; }

        /// <summary>Tenant ID (if applicable).</summary>
        public string? TenantId { get; }

        /// <summary>User ID who performed the action.</summary>
        public string? UserId { get; }

        /// <summary>Action performed (e.g., CREATE_INDEX, UPDATE_POLICY).</summary>
        public string Action { get; }

        /// <summary>Resource type (e.g., Index, Tenant, Cache, Policy).</summary>
        public string ResourceType { get; }

        /// <summary>Resource identifier.</summary>
        public string? ResourceId { get; }

        /// <summary>Additional details (JSON serialized).</summary>
        public string? Details { get; }

        /// <summary>Client IP address (if available).</summary>
        public string? IpAddress { get; }

        /// <summary>Whether the operation was successful.</summary>
        public bool Success { get; }

        public AuditEvent(
            string action,
            string resourceType,
            string? tenantId = null,
            string? userId = null,
            string? resourceId = null,
            string? details = null,
            string? ipAddress = null,
            bool success = true)
        {
            if (string.IsNullOrWhiteSpace(action))
                throw new ArgumentException("Action cannot be empty.", nameof(action));
            if (string.IsNullOrWhiteSpace(resourceType))
                throw new ArgumentException("ResourceType cannot be empty.", nameof(resourceType));

            EventId = Guid.NewGuid().ToString("N");
            Timestamp = DateTimeOffset.UtcNow;
            Action = action;
            ResourceType = resourceType;
            TenantId = tenantId;
            UserId = userId;
            ResourceId = resourceId;
            Details = details;
            IpAddress = ipAddress;
            Success = success;
        }
    }

    /// <summary>
    /// Standard audit actions.
    /// </summary>
    public static class AuditActions
    {
        // Index operations
        public const string CreateIndex = "CREATE_INDEX";
        public const string DeleteIndex = "DELETE_INDEX";
        public const string BuildIndex = "BUILD_INDEX";
        public const string SnapshotIndex = "SNAPSHOT_INDEX";
        public const string LoadIndex = "LOAD_INDEX";

        // Tenant operations
        public const string CreateTenant = "CREATE_TENANT";
        public const string UpdateQuotas = "UPDATE_QUOTAS";
        public const string UpdateApiKey = "UPDATE_API_KEY";

        // User operations
        public const string CreateUser = "CREATE_USER";
        public const string UpdateUserRole = "UPDATE_USER_ROLE";
        public const string DeleteUser = "DELETE_USER";

        // Cache operations
        public const string FlushCache = "FLUSH_CACHE";
        public const string InvalidateCache = "INVALIDATE_CACHE";
        public const string UpdatePolicy = "UPDATE_POLICY";

        // Model operations
        public const string DeployModel = "DEPLOY_MODEL";
        public const string RollbackModel = "ROLLBACK_MODEL";
    }

    /// <summary>
    /// Resource types for audit logging.
    /// </summary>
    public static class AuditResourceTypes
    {
        public const string Index = "Index";
        public const string Tenant = "Tenant";
        public const string User = "User";
        public const string Cache = "Cache";
        public const string Policy = "Policy";
        public const string Model = "Model";
    }
}
