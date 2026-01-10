using System;
using System.Collections.Generic;
using System.Linq;

namespace Pyrope.Benchmarks.Stats;

public sealed record LatencySummary(
    int Count,
    double MinMs,
    double MeanMs,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    double MaxMs)
{
    public static LatencySummary FromMilliseconds(IEnumerable<double> samplesMs)
    {
        if (samplesMs is null) throw new ArgumentNullException(nameof(samplesMs));

        var samples = samplesMs.ToArray();
        if (samples.Length == 0)
        {
            throw new ArgumentException("samplesMs must not be empty.", nameof(samplesMs));
        }

        Array.Sort(samples);

        var count = samples.Length;
        var min = samples[0];
        var max = samples[^1];
        var mean = samples.Average();

        return new LatencySummary(
            count,
            min,
            mean,
            QuantileNearestRank(samples, 0.50),
            QuantileNearestRank(samples, 0.95),
            QuantileNearestRank(samples, 0.99),
            max);
    }

    private static double QuantileNearestRank(IReadOnlyList<double> sorted, double q)
    {
        if (sorted.Count == 0) return 0;
        if (q <= 0) return sorted[0];
        if (q >= 1) return sorted[^1];

        // Nearest-rank definition: ceil(q * N)-1 (0-based)
        var n = sorted.Count;
        var idx = (int)Math.Ceiling(q * n) - 1;
        if (idx < 0) idx = 0;
        if (idx >= n) idx = n - 1;
        return sorted[idx];
    }
}

