using System;
using Pyrope.GarnetServer.Services;

namespace Pyrope.GarnetServer.Security
{
    public sealed class TenantApiKeyAuthenticator : ITenantAuthenticator
    {
        private readonly TenantRegistry _registry;

        public TenantApiKeyAuthenticator(TenantRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public bool TryAuthenticate(string tenantId, string? apiKey, out string? errorMessage)
        {
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                errorMessage = "Missing API key.";
                return false;
            }

            if (!_registry.TryGet(tenantId, out var config) || config == null)
            {
                errorMessage = "Tenant not found.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(config.ApiKey))
            {
                errorMessage = "Tenant API key not configured.";
                return false;
            }

            if (!string.Equals(apiKey, config.ApiKey, StringComparison.Ordinal))
            {
                errorMessage = "Invalid API key.";
                return false;
            }

            return true;
        }
    }
}

