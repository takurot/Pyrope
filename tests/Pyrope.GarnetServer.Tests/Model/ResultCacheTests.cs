using System;
using System.Collections.Generic;
using System.Text;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Services;
using Pyrope.GarnetServer.Vector;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Model
{
    public class ResultCacheTests
    {
        private class MockCacheStorage : ICacheStorage
        {
            public Dictionary<string, (byte[] Data, DateTime? Expiry)> Store = new();

            public bool TryGet(string key, out byte[]? value)
            {
                if (Store.TryGetValue(key, out var item))
                {
                    if (item.Expiry.HasValue && item.Expiry.Value < DateTime.UtcNow)
                    {
                        Store.Remove(key);
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
                Store[key] = (value, expiry);
            }
        }

        [Fact]
        public void Set_ShouldStoreData()
        {
            var storage = new MockCacheStorage();
            var registry = new VectorIndexRegistry();
            var cache = new ResultCache(storage, registry);
            
            var key = CreateKey("t1", "idx1");
            var result = "[]";
            
            cache.Set(key, result);
            
            Assert.True(cache.TryGet(key, out var cachedResult));
            Assert.Equal(result, cachedResult);
        }

        [Fact]
        public void TryGet_ShouldReturnFalse_WhenKeyDoesNotExist()
        {
            var storage = new MockCacheStorage();
            var registry = new VectorIndexRegistry();
            var cache = new ResultCache(storage, registry);
            
            var key = CreateKey("t1", "idx1");
            
            Assert.False(cache.TryGet(key, out _));
        }

        [Fact]
        public void TryGet_ShouldReturnFalse_WhenEpochMismatch()
        {
            var storage = new MockCacheStorage();
            var registry = new VectorIndexRegistry();
            
            // Initialize index and get initial epoch (1)
            registry.GetOrCreate("t1", "idx1", 128, VectorMetric.L2);
            
            var cache = new ResultCache(storage, registry);
            var key = CreateKey("t1", "idx1");
            
            // Cache at current epoch
            cache.Set(key, "[]");
            
            // Increment epoch (simulate update/delete)
            registry.IncrementEpoch("t1", "idx1");
            
            // Should be invalid
            Assert.False(cache.TryGet(key, out _));
        }

        private QueryKey CreateKey(string tenantId, string indexName)
        {
            return new QueryKey(
                tenantId,
                indexName,
                new float[128],
                10,
                VectorMetric.L2,
                null
            );
        }
    }
}
