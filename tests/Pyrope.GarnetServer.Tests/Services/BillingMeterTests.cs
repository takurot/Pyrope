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
            var meter = new BillingMeter(new BillingOptions { CostUnitSeconds = 0.5m });

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
            var meter = new BillingMeter(new BillingOptions { CostUnitSeconds = 2.0m });

            meter.RecordCompute("tenant-a", 1.5m);
            meter.RecordCompute("tenant-a", 0.5m);

            Assert.True(meter.TryGetUsage("tenant-a", out var usage));

            Assert.Equal(2.0m, usage.ComputeCostUnits);
            Assert.Equal(4.0m, usage.ComputeSeconds);
        }

        [Fact]
        public void RecordCompute_AccumulatesWithExactDecimalPrecision()
        {
            var meter = new BillingMeter(new BillingOptions { CostUnitSeconds = 1.0m });

            // 0.1 + 0.2 in floating point produces 0.30000000000000004
            // In decimal, 10 iterations of (0.1m + 0.2m) must be exactly 3.0m
            for (int i = 0; i < 10; i++)
            {
                meter.RecordCompute("tenant-exact", 0.1m);
                meter.RecordCompute("tenant-exact", 0.2m);
            }

            Assert.True(meter.TryGetUsage("tenant-exact", out var usage));
            Assert.Equal(3.0m, usage.ComputeCostUnits);
            Assert.Equal(3.0m, usage.ComputeSeconds);
        }

        [Fact]
        public void RecordCompute_DoubleOverload_GuardsAgainstNaNAndInfinity()
        {
            var meter = new BillingMeter(new BillingOptions { CostUnitSeconds = 1.0m });

            // NaN and Infinity must be ignored without throwing OverflowException
            meter.RecordCompute("tenant-guard", double.NaN);
            meter.RecordCompute("tenant-guard", double.PositiveInfinity);
            meter.RecordCompute("tenant-guard", double.NegativeInfinity);
            meter.RecordCompute("tenant-guard", -5.0);

            Assert.False(meter.TryGetUsage("tenant-guard", out _));

            // Valid double still works
            meter.RecordCompute("tenant-guard", 1.25);
            Assert.True(meter.TryGetUsage("tenant-guard", out var usage));
            Assert.Equal(1.25m, usage.ComputeCostUnits);
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
