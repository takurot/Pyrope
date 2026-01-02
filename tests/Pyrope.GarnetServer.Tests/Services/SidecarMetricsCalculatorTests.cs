using System;
using Pyrope.GarnetServer.Services;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Services
{
    public class SidecarMetricsCalculatorTests
    {
        [Fact]
        public void Calculate_ShouldComputeQpsAndMissRate()
        {
            var previous = new MetricsSnapshot(10, 5, 0, 0, new long[] { 5, 0, 0, 0, 0, 0 });
            var current = new MetricsSnapshot(20, 15, 0, 0, new long[] { 5, 5, 0, 0, 0, 0 });

            var prevSystem = new SystemUsageSnapshot(
                DateTimeOffset.FromUnixTimeMilliseconds(0),
                TimeSpan.FromSeconds(1));
            var currSystem = new SystemUsageSnapshot(
                DateTimeOffset.FromUnixTimeMilliseconds(10_000),
                TimeSpan.FromSeconds(5));

            var report = SidecarMetricsCalculator.Calculate(
                current,
                previous,
                currSystem,
                prevSystem,
                processorCount: 2,
                gpuUtilization: -1);

            Assert.Equal(2.0, report.Qps, 3);
            Assert.Equal(0.5, report.MissRate, 3);
            Assert.Equal(5, report.LatencyP99Ms);
            Assert.True(report.CpuUtilization > 0);
        }
    }
}
