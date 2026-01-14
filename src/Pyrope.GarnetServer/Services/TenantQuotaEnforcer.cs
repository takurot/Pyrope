using System;
using System.Collections.Concurrent;
using Pyrope.GarnetServer.Extensions;

namespace Pyrope.GarnetServer.Services
{
    public interface ITenantQuotaEnforcer
    {
        bool TryBeginRequest(string tenantId, out TenantRequestLease? lease, out string? errorCode, out string? errorMessage);
        void RecordCost(string tenantId, double cost);
        bool IsOverBudget(string tenantId);
    }

    public sealed class TenantRequestLease : IDisposable
    {
        private readonly Action _onDispose;
        private bool _disposed;

        public TenantRequestLease(Action onDispose)
        {
            _onDispose = onDispose ?? throw new ArgumentNullException(nameof(onDispose));
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _onDispose();
        }
    }

    public sealed class TenantQuotaEnforcer : ITenantQuotaEnforcer
    {
        private readonly TenantRegistry _registry;
        private readonly ITimeProvider _timeProvider;
        private readonly ConcurrentDictionary<string, TenantQpsState> _qpsStates = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, TenantConcurrencyState> _concurrencyStates = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, TenantCostState> _costStates = new(StringComparer.Ordinal);

        public TenantQuotaEnforcer(TenantRegistry registry, ITimeProvider timeProvider)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        }

        public bool TryBeginRequest(string tenantId, out TenantRequestLease? lease, out string? errorCode, out string? errorMessage)
        {
            lease = null;
            errorCode = null;
            errorMessage = null;

            if (!_registry.TryGet(tenantId, out var config) || config == null)
            {
                return true;
            }

            var quotas = config.Quotas;
            if (quotas == null)
            {
                return true;
            }

            if (quotas.MaxQps.HasValue)
            {
                var maxQps = quotas.MaxQps.Value;
                if (maxQps <= 0 || !TryConsumeQps(tenantId, maxQps))
                {
                    errorCode = VectorErrorCodes.Quota;
                    errorMessage = "Tenant QPS limit exceeded.";
                    return false;
                }
            }

            if (quotas.MaxConcurrentRequests.HasValue)
            {
                var maxConcurrent = quotas.MaxConcurrentRequests.Value;
                if (maxConcurrent <= 0 || !TryEnterConcurrent(tenantId, maxConcurrent))
                {
                    errorCode = VectorErrorCodes.Busy;
                    errorMessage = "Tenant concurrency limit exceeded.";
                    return false;
                }

                lease = new TenantRequestLease(() => ReleaseConcurrent(tenantId));
            }

            return true;
        }

        public void RecordCost(string tenantId, double cost)
        {
            if (cost <= 0) return;
            var state = _costStates.GetOrAdd(tenantId, _ => new TenantCostState());

            // Simple monthly window logic: simplified to 30 days rolling for now or just monotonic
            // Since we don't have persistent state, let's just accumulate in-memory with a reset on month change?
            // For MVP P6-6, we'll just check if current accumulated > budget.
            // But we need to know when to reset.
            // Let's use simple logic: Reset if Day difference > 30 (just as placeholder)
            // Or better: CurrentMonth logic.

            var now = DateTimeOffset.UtcNow;
            var currentMonth = now.Month;
            
            lock (state.Sync)
            {
                if (state.Month != currentMonth)
                {
                    state.Month = currentMonth;
                    state.Accumulated = 0;
                }
                state.Accumulated += cost;
            }
        }

        public bool IsOverBudget(string tenantId)
        {
            if (!_registry.TryGet(tenantId, out var config) || config?.Quotas?.MonthlyBudget == null)
            {
                return false;
            }

            if (!_costStates.TryGetValue(tenantId, out var state))
            {
                return false;
            }

            var budget = config.Quotas.MonthlyBudget.Value;
            lock (state.Sync)
            {
                return state.Accumulated > budget;
            }
        }

        private bool TryConsumeQps(string tenantId, int maxQps)
        {
            var state = _qpsStates.GetOrAdd(tenantId, _ => new TenantQpsState());
            var nowSeconds = _timeProvider.GetUnixTimeSeconds();

            lock (state.Sync)
            {
                if (state.WindowSeconds != nowSeconds)
                {
                    state.WindowSeconds = nowSeconds;
                    state.Count = 0;
                }

                if (state.Count >= maxQps)
                {
                    return false;
                }

                state.Count++;
                return true;
            }
        }

        private bool TryEnterConcurrent(string tenantId, int maxConcurrent)
        {
            var state = _concurrencyStates.GetOrAdd(tenantId, _ => new TenantConcurrencyState());
            lock (state.Sync)
            {
                if (state.Current >= maxConcurrent)
                {
                    return false;
                }

                state.Current++;
                return true;
            }
        }

        private void ReleaseConcurrent(string tenantId)
        {
            if (!_concurrencyStates.TryGetValue(tenantId, out var state))
            {
                return;
            }

            lock (state.Sync)
            {
                if (state.Current > 0)
                {
                    state.Current--;
                }
            }
        }

        private sealed class TenantQpsState
        {
            public long WindowSeconds { get; set; }
            public int Count { get; set; }
            public object Sync { get; } = new();
        }

        private sealed class TenantConcurrencyState
        {
            public int Current { get; set; }
            public object Sync { get; } = new();
        }

        private sealed class TenantCostState
        {
            public int Month { get; set; }
            public double Accumulated { get; set; }
            public object Sync { get; } = new();
        }
    }
}
