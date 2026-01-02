using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Services;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Model
{
    public class MemoryCacheStorageTests
    {
        [Fact]
        public void Set_RespectsTenantCacheLimit()
        {
            var registry = new TenantRegistry();
            registry.TryCreate("tenant1", new TenantQuota { CacheMemoryMb = 1 }, out _);
            var cache = new MemoryCacheStorage(registry);

            var key1 = "cache:tenant1:index1:1";
            var key2 = "cache:tenant1:index1:2";

            cache.Set(key1, new byte[700 * 1024]);
            cache.Set(key2, new byte[700 * 1024]);

            Assert.True(cache.TryGet(key1, out _));
            Assert.False(cache.TryGet(key2, out _));
        }
    }
}
