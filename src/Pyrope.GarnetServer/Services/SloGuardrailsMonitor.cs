using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Pyrope.GarnetServer.Services
{
    public sealed class SloGuardrailsMonitor : BackgroundService
    {
        private static readonly double[] BucketUpperBoundsMs = { 1, 5, 10, 50, 100, 200 };

        private readonly IMetricsCollector _metrics;
        private readonly ISloGuardrails _guardrails;
        private readonly IOptions<SloGuardrailsOptions> _options;
        private readonly ILogger<SloGuardrailsMonitor> _logger;

        public SloGuardrailsMonitor(
            IMetricsCollector metrics,
            ISloGuardrails guardrails,
            IOptions<SloGuardrailsOptions> options,
            ILogger<SloGuardrailsMonitor> logger)
        {
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _guardrails = guardrails ?? throw new ArgumentNullException(nameof(guardrails));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var previous = _metrics.GetSnapshot();

            while (!stoppingToken.IsCancellationRequested)
            {
                var cfg = _options.Value;
                var interval = TimeSpan.FromSeconds(Math.Max(1, cfg.MonitorIntervalSeconds));

                try
                {
                    await Task.Delay(interval, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }

                var current = _metrics.GetSnapshot();
                var (p99Ms, samples) = EstimateLatencyP99Ms(current, previous);
                previous = current;

                if (!cfg.Enabled)
                {
                    _guardrails.UpdateLatencyP99(0);
                    continue;
                }

                if (samples < Math.Max(1, cfg.MinSamplesPerInterval))
                {
                    // Not enough samples; keep previous state but update last P99 for visibility.
                    _guardrails.UpdateLatencyP99(p99Ms);
                    continue;
                }

                var wasDegraded = _guardrails.IsDegraded;
                _guardrails.UpdateLatencyP99(p99Ms);
                var isDegraded = _guardrails.IsDegraded;

                if (wasDegraded != isDegraded)
                {
                    _logger.LogWarning("SLO guardrails state changed: degraded={Degraded} p99_ms={P99Ms:0.###} samples={Samples}", isDegraded, p99Ms, samples);
                }
            }
        }

        private static (double P99Ms, long Samples) EstimateLatencyP99Ms(MetricsSnapshot current, MetricsSnapshot previous)
        {
            var bucketCount = Math.Min(current.LatencyBuckets.Length, previous.LatencyBuckets.Length);
            if (bucketCount <= 0)
            {
                return (0, 0);
            }

            long total = 0;
            var deltas = new long[bucketCount];

            for (int i = 0; i < bucketCount; i++)
            {
                var delta = current.LatencyBuckets[i] - previous.LatencyBuckets[i];
                deltas[i] = Math.Max(0, delta);
                total += deltas[i];
            }

            if (total <= 0)
            {
                return (0, 0);
            }

            long cumulative = 0;
            for (int i = 0; i < bucketCount; i++)
            {
                cumulative += deltas[i];
                if (cumulative / (double)total >= 0.99)
                {
                    return (BucketUpperBoundsMs[Math.Min(i, BucketUpperBoundsMs.Length - 1)], total);
                }
            }

            return (BucketUpperBoundsMs[^1], total);
        }
    }
}

