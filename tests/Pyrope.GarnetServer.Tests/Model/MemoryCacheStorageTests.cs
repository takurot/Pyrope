using System;
using System.Threading;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Services;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Model
{
    public class MemoryCacheStorageTests
    {
        [Fact]
        public void Set_ReturnsTrue_OnSuccess_And_False_OnQuotaBreach()
        {
            var registry = new TenantRegistry();
            registry.TryCreate("tenant1", new TenantQuota { CacheMemoryMb = 1 }, out _);
            var cache = new MemoryCacheStorage(registry);

            var key1 = "cache:tenant1:index1:1";
            var key2 = "cache:tenant1:index1:2";

            bool res1 = cache.Set(key1, new byte[700 * 1024]);
            bool res2 = cache.Set(key2, new byte[700 * 1024]);

            Assert.True(res1);
            Assert.False(res2);

            Assert.True(cache.TryGet(key1, out _));
            Assert.False(cache.TryGet(key2, out _));
        }

        [Fact]
        public void Set_And_TryGet_PerformDefensiveCopies_ProtectingAgainstBufferMutation()
        {
            var cache = new MemoryCacheStorage();
            var key = "cache:tenant1:index1:data";

            var input = new byte[] { 1, 2, 3, 4 };
            bool setOk = cache.Set(key, input);
            Assert.True(setOk);

            // Mutate input array after Set
            input[0] = 99;

            // Retrieved buffer must not reflect caller's mutation
            Assert.True(cache.TryGet(key, out var retrieved));
            Assert.NotNull(retrieved);
            Assert.Equal(1, retrieved![0]);

            // Mutate retrieved array
            retrieved[0] = 77;

            // Subsequent TryGet must still return original data
            Assert.True(cache.TryGet(key, out var retrievedAgain));
            Assert.NotNull(retrievedAgain);
            Assert.Equal(1, retrievedAgain![0]);
        }

        [Fact]
        public void ExpiredEntries_DoNotPermanentlyInflateTenantUsage_And_ArePrunedToUnblockWrites()
        {
            var registry = new TenantRegistry();
            registry.TryCreate("tenant-expire", new TenantQuota { CacheMemoryMb = 1 }, out _);
            var cache = new MemoryCacheStorage(registry);

            var key1 = "cache:tenant-expire:idx:1";
            var key2 = "cache:tenant-expire:idx:2";

            // Add 700KB with short TTL
            bool res1 = cache.Set(key1, new byte[700 * 1024], TimeSpan.FromMilliseconds(20));
            Assert.True(res1);

            // Wait for key1 to expire
            Thread.Sleep(50);

            // Prior to reading key1, GetTenantUsageBytes must prune expired entries and report 0
            long reportedUsage = cache.GetTenantUsageBytes("tenant-expire");
            Assert.Equal(0, reportedUsage);

            // A new 700KB write must succeed because expired entry is pruned rather than locking the tenant out
            bool res2 = cache.Set(key2, new byte[700 * 1024]);
            Assert.True(res2);
            Assert.True(cache.TryGet(key2, out _));
        }

        [Fact]
        public void PurgeExpired_RemovesExpiredEntriesAcrossTenants()
        {
            var registry = new TenantRegistry();
            registry.TryCreate("t1", new TenantQuota { CacheMemoryMb = 5 }, out _);
            registry.TryCreate("t2", new TenantQuota { CacheMemoryMb = 5 }, out _);
            var cache = new MemoryCacheStorage(registry);

            cache.Set("cache:t1:idx:exp", new byte[100], TimeSpan.FromMilliseconds(10));
            cache.Set("cache:t2:idx:exp", new byte[200], TimeSpan.FromMilliseconds(10));
            cache.Set("cache:t1:idx:active", new byte[300], TimeSpan.FromMinutes(10));

            Thread.Sleep(30);

            int purged = cache.PurgeExpired();
            Assert.Equal(2, purged);

            Assert.False(cache.TryGet("cache:t1:idx:exp", out _));
            Assert.False(cache.TryGet("cache:t2:idx:exp", out _));
            Assert.True(cache.TryGet("cache:t1:idx:active", out _));

            var usages = cache.GetAllTenantUsageBytes();
            Assert.Equal(300, usages["t1"]);
            Assert.False(usages.ContainsKey("t2") && usages["t2"] > 0);
        }

        [Fact]
        public void Set_And_TryGet_WithInvalidInputs_ReturnFalseSafely()
        {
            var cache = new MemoryCacheStorage();

            Assert.False(cache.Set("", new byte[10]));
            Assert.False(cache.Set(null!, new byte[10]));
            Assert.False(cache.Set("valid-key", null!));

            Assert.False(cache.TryGet("", out var v1));
            Assert.Null(v1);

            Assert.False(cache.TryGet(null!, out var v2));
            Assert.Null(v2);
        }
    }
}
