using Pyrope.Benchmarks.Stats;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Benchmarks;

public sealed class LatencySummaryTests
{
    [Fact]
    public void FromMilliseconds_ComputesNearestRankQuantiles()
    {
        var summary = LatencySummary.FromMilliseconds(new double[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });

        Assert.Equal(10, summary.Count);
        Assert.Equal(1, summary.MinMs);
        Assert.Equal(10, summary.MaxMs);
        Assert.Equal(5.5, summary.MeanMs, 10);
        Assert.Equal(5, summary.P50Ms);
        Assert.Equal(10, summary.P95Ms);
        Assert.Equal(10, summary.P99Ms);
    }
}

