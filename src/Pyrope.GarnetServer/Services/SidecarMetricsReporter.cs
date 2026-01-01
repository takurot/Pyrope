using System;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pyrope.Policy;

namespace Pyrope.GarnetServer.Services
{
    public sealed class SidecarMetricsReporter : BackgroundService
    {
        private readonly IMetricsCollector _metricsCollector;
        private readonly ISystemUsageProvider _systemUsageProvider;
        private readonly ILogger<SidecarMetricsReporter> _logger;
        private readonly string? _sidecarEndpoint;
        private TimeSpan _reportInterval;

        public SidecarMetricsReporter(
            IMetricsCollector metricsCollector,
            ISystemUsageProvider systemUsageProvider,
            IConfiguration configuration,
            ILogger<SidecarMetricsReporter> logger)
        {
            _metricsCollector = metricsCollector;
            _systemUsageProvider = systemUsageProvider;
            _logger = logger;
            _sidecarEndpoint = configuration["Sidecar:Endpoint"] ?? Environment.GetEnvironmentVariable("PYROPE_SIDECAR_ENDPOINT");

            if (!int.TryParse(configuration["Sidecar:MetricsIntervalSeconds"], out var seconds))
            {
                seconds = 10;
            }

            _reportInterval = TimeSpan.FromSeconds(Math.Max(1, seconds));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (string.IsNullOrWhiteSpace(_sidecarEndpoint))
            {
                _logger.LogInformation("Sidecar metrics reporting disabled (no endpoint configured).");
                return;
            }

            using var channel = GrpcChannel.ForAddress(_sidecarEndpoint);
            var client = new PolicyService.PolicyServiceClient(channel);

            var previousMetrics = _metricsCollector.GetSnapshot();
            var previousSystem = _systemUsageProvider.GetSnapshot();

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_reportInterval, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }

                var currentMetrics = _metricsCollector.GetSnapshot();
                var currentSystem = _systemUsageProvider.GetSnapshot();

                var report = SidecarMetricsCalculator.Calculate(
                    currentMetrics,
                    previousMetrics,
                    currentSystem,
                    previousSystem,
                    _systemUsageProvider.ProcessorCount,
                    gpuUtilization: -1);

                previousMetrics = currentMetrics;
                previousSystem = currentSystem;

                var request = new SystemMetricsRequest
                {
                    Qps = report.Qps,
                    MissRate = report.MissRate,
                    LatencyP99Ms = report.LatencyP99Ms,
                    CpuUtilization = report.CpuUtilization,
                    GpuUtilization = report.GpuUtilization,
                    CacheHitTotal = report.CacheHitTotal,
                    CacheMissTotal = report.CacheMissTotal,
                    TimestampUnixMs = report.TimestampUnixMs
                };

                try
                {
                    var response = await client.ReportSystemMetricsAsync(request, cancellationToken: stoppingToken);
                    if (response.NextReportIntervalMs > 0)
                    {
                        _reportInterval = TimeSpan.FromMilliseconds(response.NextReportIntervalMs);
                    }
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Failed to report metrics to sidecar");
                }
            }
        }
    }
}
