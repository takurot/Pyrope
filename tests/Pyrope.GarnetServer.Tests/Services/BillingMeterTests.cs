using System;
using System.Collections.Generic;
using System.IO;
using Pyrope.GarnetServer.Services;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Services
{
    public class BillingMeterTests
    {
        [Fact]
        public void RecordRequest_TracksHitsAndMisses()
        {
            var meter = new BillingMeter(new BillingOptions { CostUnitSeconds = 0.5 });

            meter.RecordRequest("tenant-a", cacheHit: true);
            meter.RecordRequest("tenant-a", cacheHit: false);
            meter.RecordRequest("tenant-a", cacheHit: false);

            Assert.True(meter.TryGetUsage("tenant-a", out var usage));

            Assert.Equal(3, usage.RequestsTotal);
            Assert.Equal(1, usage.CacheHits);
            Assert.Equal(2, usage.CacheMisses);
        }

        [Fact]
        public void RecordCompute_AccumulatesCostAndSeconds()
        {
            var meter = new BillingMeter(new BillingOptions { CostUnitSeconds = 2.0 });

            meter.RecordCompute("tenant-a", 1.5);
            meter.RecordCompute("tenant-a", 0.5);

            Assert.True(meter.TryGetUsage("tenant-a", out var usage));

            Assert.Equal(2.0, usage.ComputeCostUnits, 3);
            Assert.Equal(4.0, usage.ComputeSeconds, 3);
        }

        [Fact]
        public void RecordStorage_AccumulatesVectorAndSnapshotBytes()
        {
            var meter = new BillingMeter(new BillingOptions());

            meter.RecordVectorBytes("tenant-a", 100);
            meter.RecordVectorBytes("tenant-a", -40);

            var tempDir = Path.Combine(Path.GetTempPath(), "pyrope_billing_meter_tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            var snapshotPath = Path.Combine(tempDir, "snapshot.bin");
            File.WriteAllBytes(snapshotPath, new byte[10]);
            File.WriteAllBytes(snapshotPath + ".head", new byte[5]);

            meter.RecordSnapshot("tenant-a", "index-a", snapshotPath);

            Assert.True(meter.TryGetUsage("tenant-a", out var usage));

            Assert.Equal(60, usage.VectorStorageBytes);
            Assert.Equal(15, usage.SnapshotStorageBytes);

            File.WriteAllBytes(snapshotPath + ".tail", new byte[2]);
            meter.RecordSnapshot("tenant-a", "index-a", snapshotPath);

            Assert.True(meter.TryGetUsage("tenant-a", out var updated));
            Assert.Equal(17, updated.SnapshotStorageBytes);
        }
    }
}
