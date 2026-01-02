using System;
using System.Collections.Concurrent;
using Pyrope.GarnetServer.Services;

namespace Pyrope.GarnetServer.Model
{
    public class MemoryCacheStorage : ICacheStorage, ICacheAdmin
    {
        private readonly ConcurrentDictionary<string, CacheEntry> _store = new();
        private readonly ConcurrentDictionary<string, long> _tenantUsageBytes = new(StringComparer.Ordinal);
        private readonly object _sync = new();
        private readonly TenantRegistry? _tenantRegistry;

        public MemoryCacheStorage(TenantRegistry? tenantRegistry = null)
        {
            _tenantRegistry = tenantRegistry;
        }

        public bool TryGet(string key, out byte[]? value)
        {
            if (_store.TryGetValue(key, out var item))
            {
                if (item.Expiry.HasValue && item.Expiry.Value < DateTime.UtcNow)
                {
                    RemoveEntry(key, item);
                    value = null;
                    return false;
                }
                value = item.Data;
                return true;
            }
            value = null;
            return false;
        }

        public void Set(string key, byte[] value, TimeSpan? ttl = null)
        {
            DateTime? expiry = ttl.HasValue ? DateTime.UtcNow.Add(ttl.Value) : null;
            var sizeBytes = value.Length;
            var hasTenant = TryGetTenantFromKey(key, out var tenantId);

            lock (_sync)
            {
                CacheEntry? existing = null;
                if (_store.TryGetValue(key, out var current))
                {
                    if (current.Expiry.HasValue && current.Expiry.Value < DateTime.UtcNow)
                    {
                        _store.TryRemove(key, out _);
                        AdjustUsage(current.TenantId, -current.SizeBytes);
                    }
                    else
                    {
                        existing = current;
                    }
                }

                if (hasTenant && _tenantRegistry != null && _tenantRegistry.TryGet(tenantId, out var config))
                {
                    var limitMb = config?.Quotas?.CacheMemoryMb;
                    if (limitMb.HasValue)
                    {
                        var limitBytes = limitMb.Value * 1024L * 1024L;
                        var currentUsage = _tenantUsageBytes.TryGetValue(tenantId, out var usage) ? usage : 0;
                        var projected = currentUsage - (existing?.SizeBytes ?? 0) + sizeBytes;
                        if (projected > limitBytes)
                        {
                            return;
                        }
                    }
                }

                _store[key] = new CacheEntry(value, expiry, sizeBytes, hasTenant ? tenantId : null);

                if (existing != null)
                {
                    AdjustUsage(existing.TenantId, -existing.SizeBytes);
                }

                if (hasTenant)
                {
                    AdjustUsage(tenantId, sizeBytes);
                }
            }
        }

        public int Clear()
        {
            lock (_sync)
            {
                var count = _store.Count;
                _store.Clear();
                _tenantUsageBytes.Clear();
                return count;
            }
        }

        public int RemoveByPrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix))
            {
                return 0;
            }

            var removed = 0;
            lock (_sync)
            {
                foreach (var key in _store.Keys)
                {
                    if (!key.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (_store.TryRemove(key, out var entry))
                    {
                        AdjustUsage(entry.TenantId, -entry.SizeBytes);
                        removed++;
                    }
                }
            }

            return removed;
        }

        private void RemoveEntry(string key, CacheEntry entry)
        {
            lock (_sync)
            {
                if (_store.TryRemove(key, out var removed))
                {
                    AdjustUsage(removed.TenantId, -removed.SizeBytes);
                }
            }
        }

        private void AdjustUsage(string? tenantId, long delta)
        {
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return;
            }

            _tenantUsageBytes.AddOrUpdate(
                tenantId,
                _ => Math.Max(0, delta),
                (_, current) => Math.Max(0, current + delta));
        }

        private static bool TryGetTenantFromKey(string key, out string tenantId)
        {
            tenantId = "";
            if (!key.StartsWith("cache:", StringComparison.Ordinal))
            {
                return false;
            }

            var parts = key.Split(':', 4, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                return false;
            }

            tenantId = parts[1];
            return !string.IsNullOrWhiteSpace(tenantId);
        }

        private sealed class CacheEntry
        {
            public CacheEntry(byte[] data, DateTime? expiry, int sizeBytes, string? tenantId)
            {
                Data = data;
                Expiry = expiry;
                SizeBytes = sizeBytes;
                TenantId = tenantId;
            }

            public byte[] Data { get; }
            public DateTime? Expiry { get; }
            public int SizeBytes { get; }
            public string? TenantId { get; }
        }
    }
}
