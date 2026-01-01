using System;

namespace Pyrope.GarnetServer.Services
{
    public sealed record SidecarMetricsReport(
        double Qps,
        double MissRate,
        double LatencyP99Ms,
        double CpuUtilization,
        double GpuUtilization,
        long CacheHitTotal,
        long CacheMissTotal,
        long TimestampUnixMs);

    public static class SidecarMetricsCalculator
    {
        private static readonly double[] LatencyBucketUpperBoundsMs = { 1, 5, 10, 50, 100, 200 };

        public static SidecarMetricsReport Calculate(
            MetricsSnapshot current,
            MetricsSnapshot previous,
            SystemUsageSnapshot currentSystem,
            SystemUsageSnapshot previousSystem,
            int processorCount,
            double gpuUtilization)
        {
            var deltaHits = Math.Max(0, current.CacheHits - previous.CacheHits);
            var deltaMisses = Math.Max(0, current.CacheMisses - previous.CacheMisses);
            var totalRequests = deltaHits + deltaMisses;

            var elapsedMs = Math.Max(1, (currentSystem.Timestamp - previousSystem.Timestamp).TotalMilliseconds);
            var elapsedSeconds = elapsedMs / 1000.0;
            var qps = totalRequests / elapsedSeconds;
            var missRate = totalRequests == 0 ? 0 : deltaMisses / (double)totalRequests;

            var latencyP99 = EstimateLatencyP99(current, previous);
            var cpuUtilization = EstimateCpuUtilization(previousSystem, currentSystem, processorCount);

            return new SidecarMetricsReport(
                qps,
                missRate,
                latencyP99,
                cpuUtilization,
                gpuUtilization,
                current.CacheHits,
                current.CacheMisses,
                currentSystem.Timestamp.ToUnixTimeMilliseconds());
        }

        private static double EstimateLatencyP99(MetricsSnapshot current, MetricsSnapshot previous)
        {
            var bucketCount = Math.Min(current.LatencyBuckets.Length, previous.LatencyBuckets.Length);
            long total = 0;
            var deltas = new long[bucketCount];

            for (int i = 0; i < bucketCount; i++)
            {
                var delta = current.LatencyBuckets[i] - previous.LatencyBuckets[i];
                deltas[i] = Math.Max(0, delta);
                total += deltas[i];
            }

            if (total == 0)
            {
                return 0;
            }

            long cumulative = 0;
            for (int i = 0; i < bucketCount; i++)
            {
                cumulative += deltas[i];
                if (cumulative / (double)total >= 0.99)
                {
                    return LatencyBucketUpperBoundsMs[Math.Min(i, LatencyBucketUpperBoundsMs.Length - 1)];
                }
            }

            return LatencyBucketUpperBoundsMs[^1];
        }

        private static double EstimateCpuUtilization(SystemUsageSnapshot previous, SystemUsageSnapshot current, int processorCount)
        {
            if (processorCount <= 0)
            {
                return 0;
            }

            var wallMs = (current.Timestamp - previous.Timestamp).TotalMilliseconds;
            var cpuMs = (current.CpuTime - previous.CpuTime).TotalMilliseconds;

            if (wallMs <= 0)
            {
                return 0;
            }

            var utilization = cpuMs / (wallMs * processorCount) * 100.0;
            return Math.Clamp(utilization, 0, 100);
        }
    }
}
