using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Pyrope.GarnetServer.Services;

namespace Pyrope.GarnetServer.Model
{
    public class MemoryCacheStorage : ICacheStorage, ICacheAdmin, ICacheUsageProvider
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
            if (string.IsNullOrEmpty(key))
            {
                value = null;
                return false;
            }

            if (_store.TryGetValue(key, out var item))
            {
                if (item.Expiry.HasValue && item.Expiry.Value < DateTime.UtcNow)
                {
                    RemoveEntry(key, item);
                    value = null;
                    return false;
                }
                value = (byte[])item.Data.Clone();
                return true;
            }
            value = null;
            return false;
        }

        public bool Set(string key, byte[] value, TimeSpan? ttl = null)
        {
            if (string.IsNullOrEmpty(key) || value == null)
            {
                return false;
            }

            var now = DateTime.UtcNow;
            DateTime? expiry = ttl.HasValue ? now.Add(ttl.Value) : null;
            var dataCopy = (byte[])value.Clone();
            var sizeBytes = dataCopy.Length;
            var hasTenant = TryGetTenantFromKey(key, out var tenantId);

            lock (_sync)
            {
                CacheEntry? existing = null;
                if (_store.TryGetValue(key, out var current))
                {
                    if (current.Expiry.HasValue && current.Expiry.Value < now)
                    {
                        _store.TryRemove(new KeyValuePair<string, CacheEntry>(key, current));
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
                            // Proactively prune expired entries for this tenant to unblock capacity
                            PruneExpiredForTenant(tenantId, now);

                            // Check if 'existing' entry was pruned to prevent double subtraction
                            if (existing != null && (!_store.TryGetValue(key, out var stillThere) || !ReferenceEquals(stillThere, existing)))
                            {
                                existing = null;
                            }

                            currentUsage = _tenantUsageBytes.TryGetValue(tenantId, out var recheckedUsage) ? recheckedUsage : 0;
                            projected = currentUsage - (existing?.SizeBytes ?? 0) + sizeBytes;
                            if (projected > limitBytes)
                            {
                                return false;
                            }
                        }
                    }
                }

                _store[key] = new CacheEntry(dataCopy, expiry, sizeBytes, hasTenant ? tenantId : null);

                if (existing != null)
                {
                    AdjustUsage(existing.TenantId, -existing.SizeBytes);
                }

                if (hasTenant)
                {
                    AdjustUsage(tenantId, sizeBytes);
                }

                return true;
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

        public int PurgeExpired()
        {
            var now = DateTime.UtcNow;
            int pruned = 0;
            lock (_sync)
            {
                foreach (var kvp in _store)
                {
                    var entry = kvp.Value;
                    if (entry.Expiry.HasValue && entry.Expiry.Value < now)
                    {
                        if (_store.TryRemove(new KeyValuePair<string, CacheEntry>(kvp.Key, entry)))
                        {
                            AdjustUsage(entry.TenantId, -entry.SizeBytes);
                            pruned++;
                        }
                    }
                }
            }
            return pruned;
        }

        public long GetTenantUsageBytes(string tenantId)
        {
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return 0;
            }

            lock (_sync)
            {
                PruneExpiredForTenant(tenantId, DateTime.UtcNow);
                return _tenantUsageBytes.TryGetValue(tenantId, out var usage) ? usage : 0;
            }
        }

        public IReadOnlyDictionary<string, long> GetAllTenantUsageBytes()
        {
            lock (_sync)
            {
                PurgeExpired();
                return new Dictionary<string, long>(_tenantUsageBytes);
            }
        }

        private void RemoveEntry(string key, CacheEntry expectedEntry)
        {
            lock (_sync)
            {
                if (_store.TryGetValue(key, out var current) && ReferenceEquals(current, expectedEntry))
                {
                    if (_store.TryRemove(new KeyValuePair<string, CacheEntry>(key, expectedEntry)))
                    {
                        AdjustUsage(expectedEntry.TenantId, -expectedEntry.SizeBytes);
                    }
                }
            }
        }

        private int PruneExpiredForTenant(string tenantId, DateTime now)
        {
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return 0;
            }

            int pruned = 0;
            foreach (var kvp in _store)
            {
                var entry = kvp.Value;
                if (entry.TenantId == tenantId && entry.Expiry.HasValue && entry.Expiry.Value < now)
                {
                    if (_store.TryRemove(new KeyValuePair<string, CacheEntry>(kvp.Key, entry)))
                    {
                        AdjustUsage(tenantId, -entry.SizeBytes);
                        pruned++;
                    }
                }
            }
            return pruned;
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
