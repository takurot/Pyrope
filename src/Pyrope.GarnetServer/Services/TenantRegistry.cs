using System;
using System.Collections.Concurrent;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Utils;

namespace Pyrope.GarnetServer.Services
{
    public class TenantRegistry
    {
        private readonly ConcurrentDictionary<string, TenantConfig> _tenants = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, string> _apiKeyToTenantId = new(StringComparer.Ordinal);

        public bool TryCreate(string tenantId, TenantQuota quotas, out TenantConfig? config, string? apiKey = null)
        {
            TenantNamespace.ValidateTenantId(tenantId);
            
            // Enforce API key uniqueness if provided
            if (!string.IsNullOrEmpty(apiKey))
            {
                if (!_apiKeyToTenantId.TryAdd(apiKey, tenantId))
                {
                    config = null;
                    return false;
                }
            }

            var now = DateTimeOffset.UtcNow;
            var tenantConfig = new TenantConfig(tenantId, quotas, apiKey, now);
            if (_tenants.TryAdd(tenantId, tenantConfig))
            {
                config = tenantConfig;
                return true;
            }

            // Rollback index if tenant creation failed
            if (!string.IsNullOrEmpty(apiKey))
            {
                _apiKeyToTenantId.TryRemove(apiKey, out _);
            }

            config = null;
            return false;
        }

        public bool TryGet(string tenantId, out TenantConfig? config)
        {
            TenantNamespace.TryValidateTenantId(tenantId, out _); 
            return _tenants.TryGetValue(tenantId, out config);
        }

        public bool TryGetByApiKey(string apiKey, out TenantConfig? config)
        {
            config = null;
            if (string.IsNullOrEmpty(apiKey)) return false;
            if (_apiKeyToTenantId.TryGetValue(apiKey, out var tenantId))
            {
                return _tenants.TryGetValue(tenantId, out config);
            }
            return false;
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

        public bool TryUpdateApiKey(string tenantId, string apiKey, out TenantConfig? config)
        {
            if (_tenants.TryGetValue(tenantId, out var existing))
            {
                var oldKey = existing.ApiKey;
                
                // If the key is not changing, just return success
                if (string.Equals(oldKey, apiKey, StringComparison.Ordinal))
                {
                    config = existing;
                    return true;
                }

                // Try to add new key to index
                if (!string.IsNullOrEmpty(apiKey))
                {
                    if (!_apiKeyToTenantId.TryAdd(apiKey, tenantId))
                    {
                        config = null;
                        return false;
                    }
                }

                existing.UpdateApiKey(apiKey, DateTimeOffset.UtcNow);
                
                // Remove old key from index
                if (!string.IsNullOrEmpty(oldKey))
                {
                    _apiKeyToTenantId.TryRemove(oldKey, out _);
                }

                config = existing;
                return true;
            }

            config = null;
            return false;
        }
    }
}
