using System;
using System.Collections.Concurrent;
using Pyrope.GarnetServer.Model;

namespace Pyrope.GarnetServer.Services
{
    public class TenantRegistry
    {
        private readonly ConcurrentDictionary<string, TenantConfig> _tenants = new(StringComparer.Ordinal);

        public bool TryCreate(string tenantId, TenantQuota quotas, out TenantConfig? config)
        {
            var now = DateTimeOffset.UtcNow;
            var tenantConfig = new TenantConfig(tenantId, quotas, now);
            if (_tenants.TryAdd(tenantId, tenantConfig))
            {
                config = tenantConfig;
                return true;
            }

            config = null;
            return false;
        }

        public bool TryGet(string tenantId, out TenantConfig? config)
        {
            return _tenants.TryGetValue(tenantId, out config);
        }

        public bool TryUpdateQuotas(string tenantId, TenantQuota quotas, out TenantConfig? config)
        {
            if (_tenants.TryGetValue(tenantId, out var existing))
            {
                existing.UpdateQuotas(quotas, DateTimeOffset.UtcNow);
                config = existing;
                return true;
            }

            config = null;
            return false;
        }
    }
}
