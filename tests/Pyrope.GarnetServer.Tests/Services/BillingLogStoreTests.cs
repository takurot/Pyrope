using System;
using System.IO;
using Microsoft.Extensions.Options;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Services;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Services
{
    public class BillingLogStoreTests : IDisposable
    {
        private readonly string _testDir;

        public BillingLogStoreTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "pyrope_billing_log_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testDir))
                {
                    Directory.Delete(_testDir, true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

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
                0.5m,
                0.5m,
                10,
                0,
                now);

            var entry1 = store.AppendSnapshot(firstUsage, now);

            var secondUsage = firstUsage with { RequestsTotal = 2, CacheMisses = 1, UpdatedAt = now.AddSeconds(1) };
            var entry2 = store.AppendSnapshot(secondUsage, now.AddSeconds(1));

            Assert.Equal(entry1.Hash, entry2.PrevHash);
            Assert.True(store.VerifyChain("tenant-a"));
        }

        [Fact]
        public void VerifyChain_WhenEntriesEvicted_StillVerifiesIntegrity()
        {
            // Set MaxInMemoryEntries = 3 and append 10 snapshots
            var options = Options.Create(new BillingOptions
            {
                LogIntervalSeconds = 0,
                MaxInMemoryEntries = 3
            });
            var store = new BillingLogStore(options);

            var now = DateTimeOffset.UtcNow;
            for (int i = 1; i <= 10; i++)
            {
                var usage = new TenantBillingUsage(
                    "tenant-a",
                    i,
                    i,
                    0,
                    i * 0.1m,
                    i * 0.1m,
                    100,
                    0,
                    now.AddSeconds(i));
                store.AppendSnapshot(usage, now.AddSeconds(i));
            }

            // Older entries (1-7) have been evicted from ConcurrentQueue
            var inMemoryEntries = store.Query("tenant-a", 100);
            Assert.Equal(3, inMemoryEntries.Count);

            // VerifyChain must return true for the intact in-memory window
            Assert.True(store.VerifyChain("tenant-a"));
        }

        [Fact]
        public void ProcessRestart_MaintainsHashContinuity()
        {
            var logPath = Path.Combine(_testDir, "billing.jsonl");
            var options = Options.Create(new BillingOptions
            {
                LogIntervalSeconds = 0,
                LogPath = logPath,
                MaxInMemoryEntries = 5
            });

            var store1 = new BillingLogStore(options);
            var now = DateTimeOffset.UtcNow;

            BillingLogEntry? lastEntryStore1 = null;
            for (int i = 1; i <= 3; i++)
            {
                var usage = new TenantBillingUsage(
                    "tenant-persist",
                    i,
                    i,
                    0,
                    i * 1.0m,
                    i * 1.0m,
                    100,
                    0,
                    now.AddSeconds(i));
                lastEntryStore1 = store1.AppendSnapshot(usage, now.AddSeconds(i));
            }

            Assert.NotNull(lastEntryStore1);
            Assert.True(File.Exists(logPath));

            // Simulate server restart: new instance initialized with the same logPath
            var store2 = new BillingLogStore(options);

            // Verify query returns existing entries
            var existing = store2.Query("tenant-persist", 10);
            Assert.Equal(3, existing.Count);

            // Append new snapshot after restart
            var postRestartUsage = new TenantBillingUsage(
                "tenant-persist",
                4,
                4,
                0,
                4.0m,
                4.0m,
                100,
                0,
                now.AddSeconds(4));
            var postRestartEntry = store2.AppendSnapshot(postRestartUsage, now.AddSeconds(4));

            // Cryptographic continuity check: PrevHash must match the last hash from before restart!
            Assert.Equal(lastEntryStore1.Hash, postRestartEntry.PrevHash);

            // Full chain verification in store2
            Assert.True(store2.VerifyChain("tenant-persist"));
        }
    }
}
