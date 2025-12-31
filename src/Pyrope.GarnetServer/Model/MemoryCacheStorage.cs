using System;
using System.Collections.Concurrent;

namespace Pyrope.GarnetServer.Model
{
    public class MemoryCacheStorage : ICacheStorage
    {
        private readonly ConcurrentDictionary<string, (byte[] Data, DateTime? Expiry)> _store = new();

        public bool TryGet(string key, out byte[]? value)
        {
            if (_store.TryGetValue(key, out var item))
            {
                if (item.Expiry.HasValue && item.Expiry.Value < DateTime.UtcNow)
                {
                    _store.TryRemove(key, out _);
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
            _store[key] = (value, expiry);
        }
    }
}
