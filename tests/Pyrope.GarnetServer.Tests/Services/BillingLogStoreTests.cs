using System;
using Microsoft.Extensions.Options;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Services;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Services
{
    public class BillingLogStoreTests
    {
        [Fact]
        public void AppendSnapshot_BuildsHashChain()
        {
            var options = Options.Create(new BillingOptions { LogIntervalSeconds = 0 });
            var store = new BillingLogStore(options);

            var now = DateTimeOffset.UtcNow;
            var firstUsage = new TenantBillingUsage(
                "tenant-a",
                1,
                1,
                0,
                0.5,
                0.5,
                10,
                0,
                now);

            var entry1 = store.AppendSnapshot(firstUsage, now);

            var secondUsage = firstUsage with { RequestsTotal = 2, CacheMisses = 1, UpdatedAt = now.AddSeconds(1) };
            var entry2 = store.AppendSnapshot(secondUsage, now.AddSeconds(1));

            Assert.Equal(entry1.Hash, entry2.PrevHash);
            Assert.True(store.VerifyChain("tenant-a"));
        }
    }
}
