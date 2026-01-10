using System;
using System.Threading;
using Microsoft.Extensions.Options;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Vector;

namespace Pyrope.GarnetServer.Services
{
    public sealed class SloGuardrails : ISloGuardrails
    {
        private readonly IOptions<SloGuardrailsOptions> _options;
        private readonly TenantRegistry _tenantRegistry;
        private int _isDegraded;
        private double _lastP99Ms;

        public SloGuardrails(IOptions<SloGuardrailsOptions> options, TenantRegistry tenantRegistry)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _tenantRegistry = tenantRegistry ?? throw new ArgumentNullException(nameof(tenantRegistry));
        }

        public bool IsDegraded => Volatile.Read(ref _isDegraded) == 1;
        public double LastP99Ms => Interlocked.CompareExchange(ref _lastP99Ms, 0, 0);

        public void UpdateLatencyP99(double p99Ms)
        {
            Interlocked.Exchange(ref _lastP99Ms, p99Ms);

            var cfg = _options.Value;
            if (!cfg.Enabled || cfg.TargetP99Ms <= 0)
            {
                Interlocked.Exchange(ref _isDegraded, 0);
                return;
            }

            var recoveryFactor = cfg.RecoveryFactor;
            if (double.IsNaN(recoveryFactor) || recoveryFactor <= 0 || recoveryFactor > 1)
            {
                recoveryFactor = 0.8;
            }

            var target = cfg.TargetP99Ms;
            var recoveryThreshold = target * recoveryFactor;

            var degraded = IsDegraded;
            if (!degraded && p99Ms > target)
            {
                Interlocked.Exchange(ref _isDegraded, 1);
                return;
            }

            if (degraded && p99Ms > 0 && p99Ms <= recoveryThreshold)
            {
                Interlocked.Exchange(ref _isDegraded, 0);
            }
        }

        public SearchOptions? GetSearchOptions(string tenantId, string indexName)
        {
            var cfg = _options.Value;
            if (!cfg.Enabled || !IsDegraded)
            {
                return null;
            }

            // Prefer protecting high priority tenants by not degrading their search.
            var priority = GetTenantPriority(tenantId);
            if (priority <= 0)
            {
                return null;
            }

            return new SearchOptions(MaxScans: cfg.DegradedMaxScans);
        }

        public bool ShouldForceCacheOnly(string tenantId, string indexName)
        {
            var cfg = _options.Value;
            if (!cfg.Enabled || !IsDegraded)
            {
                return false;
            }

            // Low priority tenants are shed on cache miss to protect SLO under load.
            var priority = GetTenantPriority(tenantId);
            return priority >= 2;
        }

        private int GetTenantPriority(string tenantId)
        {
            try
            {
                if (_tenantRegistry.TryGet(tenantId, out var config) && config != null)
                {
                    return config.Quotas?.Priority ?? 1;
                }
            }
            catch
            {
                // ignore validation errors; treat as normal
            }

            return 1;
        }
    }
}

