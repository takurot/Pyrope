using System;
using System.Collections.Concurrent;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Utils;

namespace Pyrope.GarnetServer.Services
{
    public class TenantRegistry
    {
        private readonly ConcurrentDictionary<string, TenantConfig> _tenants = new(StringComparer.Ordinal);

        public bool TryCreate(string tenantId, TenantQuota quotas, out TenantConfig? config, string? apiKey = null)
        {
            TenantNamespace.ValidateTenantId(tenantId);
            var now = DateTimeOffset.UtcNow;
            var tenantConfig = new TenantConfig(tenantId, quotas, apiKey, now);
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
            TenantNamespace.ValidateTenantId(tenantId);
            return _tenants.TryGetValue(tenantId, out config);
        }

        public bool TryUpdateQuotas(string tenantId, TenantQuota quotas, out TenantConfig? config)
        {
            TenantNamespace.ValidateTenantId(tenantId);
            if (_tenants.TryGetValue(tenantId, out var existing))
            {
                existing.UpdateQuotas(quotas, DateTimeOffset.UtcNow);
                config = existing;
                return true;
            }

            config = null;
            return false;
        }

        public bool TryUpdateApiKey(string tenantId, string apiKey, out TenantConfig? config)
        {
            TenantNamespace.ValidateTenantId(tenantId);
            if (_tenants.TryGetValue(tenantId, out var existing))
            {
                existing.UpdateApiKey(apiKey, DateTimeOffset.UtcNow);
                config = existing;
                return true;
            }

            config = null;
            return false;
        }
    }
}
