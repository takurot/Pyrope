using System;
using System.Collections.Concurrent;

namespace Pyrope.GarnetServer.Model
{
    public class MemoryCacheStorage : ICacheStorage, ICacheAdmin
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

        public int Clear()
        {
            var count = _store.Count;
            _store.Clear();
            return count;
        }

        public int RemoveByPrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix))
            {
                return 0;
            }

            var removed = 0;
            foreach (var key in _store.Keys)
            {
                if (!key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                if (_store.TryRemove(key, out _))
                {
                    removed++;
                }
            }

            return removed;
        }
    }
}
