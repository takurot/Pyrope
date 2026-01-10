using System;

namespace Pyrope.GarnetServer.Model
{
    public sealed class TenantConfig
    {
        public string TenantId { get; }
        public TenantQuota Quotas { get; private set; }
        public string ApiKey { get; private set; }
        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset UpdatedAt { get; private set; }

        public TenantConfig(string tenantId, TenantQuota quotas, string? apiKey, DateTimeOffset createdAt)
        {
            if (string.IsNullOrWhiteSpace(tenantId)) throw new ArgumentException("Tenant id cannot be empty.", nameof(tenantId));
            TenantId = tenantId;
            Quotas = quotas ?? throw new ArgumentNullException(nameof(quotas));
            ApiKey = apiKey ?? "";
            CreatedAt = createdAt;
            UpdatedAt = createdAt;
        }

        public void UpdateQuotas(TenantQuota quotas, DateTimeOffset updatedAt)
        {
            Quotas = quotas ?? throw new ArgumentNullException(nameof(quotas));
            UpdatedAt = updatedAt;
        }

        public void UpdateApiKey(string apiKey, DateTimeOffset updatedAt)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("API key cannot be empty.", nameof(apiKey));
            ApiKey = apiKey;
            UpdatedAt = updatedAt;
        }
    }

    public sealed class TenantQuota
    {
        public int? MaxQps { get; set; }
        public int? MaxConcurrentRequests { get; set; }
        public long? CacheMemoryMb { get; set; }
        public long? DailyRequestLimit { get; set; }

        /// <summary>
        /// Tenant priority for noisy-neighbor mitigation.
        /// 0 = high, 1 = normal, 2 = low (default).
        /// </summary>
        public int Priority { get; set; } = 1;
    }
}
