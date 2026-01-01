using System;

namespace Pyrope.GarnetServer.Model
{
    public sealed class TenantConfig
    {
        public string TenantId { get; }
        public TenantQuota Quotas { get; private set; }
        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset UpdatedAt { get; private set; }

        public TenantConfig(string tenantId, TenantQuota quotas, DateTimeOffset createdAt)
        {
            if (string.IsNullOrWhiteSpace(tenantId)) throw new ArgumentException("Tenant id cannot be empty.", nameof(tenantId));
            TenantId = tenantId;
            Quotas = quotas ?? throw new ArgumentNullException(nameof(quotas));
            CreatedAt = createdAt;
            UpdatedAt = createdAt;
        }

        public void UpdateQuotas(TenantQuota quotas, DateTimeOffset updatedAt)
        {
            Quotas = quotas ?? throw new ArgumentNullException(nameof(quotas));
            UpdatedAt = updatedAt;
        }
    }

    public sealed class TenantQuota
    {
        public int? MaxQps { get; set; }
        public int? MaxConcurrentRequests { get; set; }
        public long? CacheMemoryMb { get; set; }
        public long? DailyRequestLimit { get; set; }
    }
}
