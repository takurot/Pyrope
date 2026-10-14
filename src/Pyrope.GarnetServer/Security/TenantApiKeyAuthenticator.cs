using System;
using Microsoft.Extensions.Options;
using Pyrope.GarnetServer.Services;
using Pyrope.GarnetServer.Utils;

namespace Pyrope.GarnetServer.Security
{
    public sealed class TenantApiKeyAuthenticator : ITenantAuthenticator
    {
        private readonly TenantRegistry _registry;
        private readonly ApiKeyAuthOptions _options;

        public TenantApiKeyAuthenticator(TenantRegistry registry, IOptions<ApiKeyAuthOptions> options)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        }

        public bool TryAuthenticate(string tenantId, string? apiKey, out string? errorMessage)
        {
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(tenantId))
            {
                errorMessage = "Missing tenant ID.";
                return false;
            }

            if (!TenantNamespace.TryValidateTenantId(tenantId, out var tenantError))
            {
                errorMessage = tenantError;
                return false;
            }

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

            if (!SecurityUtils.FixedTimeEquals(apiKey, config.ApiKey))
            {
                errorMessage = "Invalid API key.";
                return false;
            }

            return true;
        }
    }
}
