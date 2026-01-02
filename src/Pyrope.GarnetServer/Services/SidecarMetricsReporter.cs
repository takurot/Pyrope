using System;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pyrope.Policy;
using Pyrope.GarnetServer.Policies;

namespace Pyrope.GarnetServer.Services
{
    public sealed class SidecarMetricsReporter : BackgroundService
    {
        private readonly IMetricsCollector _metricsCollector;
        private readonly ISystemUsageProvider _systemUsageProvider;
        private readonly IPolicyEngine _policyEngine;
        private readonly ILogger<SidecarMetricsReporter> _logger;
        private readonly PolicyService.PolicyServiceClient? _policyClient;
        private readonly string? _sidecarEndpoint;
        private TimeSpan _reportInterval;
        private readonly TimeSpan _warmPathTimeout;

        public SidecarMetricsReporter(
            IMetricsCollector metricsCollector,
            ISystemUsageProvider systemUsageProvider,
            IPolicyEngine policyEngine,
            IConfiguration configuration,
            ILogger<SidecarMetricsReporter> logger,
            PolicyService.PolicyServiceClient? policyClient = null)
        {
            _metricsCollector = metricsCollector;
            _systemUsageProvider = systemUsageProvider;
            _policyEngine = policyEngine;
            _logger = logger;
            _policyClient = policyClient;
            _sidecarEndpoint = configuration["Sidecar:Endpoint"] ?? Environment.GetEnvironmentVariable("PYROPE_SIDECAR_ENDPOINT");

            if (!int.TryParse(configuration["Sidecar:MetricsIntervalSeconds"], out var seconds))
            {
                seconds = 10;
            }

            _reportInterval = TimeSpan.FromSeconds(Math.Max(1, seconds));

            if (!int.TryParse(configuration["Sidecar:WarmPathTimeoutMs"], out var timeoutMs))
            {
                timeoutMs = 50;
            }

            _warmPathTimeout = TimeSpan.FromMilliseconds(Math.Max(1, timeoutMs));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_policyClient == null && string.IsNullOrWhiteSpace(_sidecarEndpoint))
            {
                _logger.LogInformation("Sidecar metrics reporting disabled (no endpoint configured).");
                return;
            }

            GrpcChannel? channel = null;
            var client = _policyClient;
            if (client == null)
            {
                channel = GrpcChannel.ForAddress(_sidecarEndpoint!);
                client = new PolicyService.PolicyServiceClient(channel);
            }

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
                    var response = await client.ReportSystemMetricsAsync(
                        request,
                        deadline: DateTime.UtcNow.Add(_warmPathTimeout),
                        cancellationToken: stoppingToken);
                    if (response.NextReportIntervalMs > 0)
                    {
                        _reportInterval = TimeSpan.FromMilliseconds(response.NextReportIntervalMs);
                    }

                    if (response.Policy != null)
                    {
                        _policyEngine.UpdatePolicy(response.Policy);
                    }
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded && !stoppingToken.IsCancellationRequested)
                {
                    _metricsCollector.RecordAiFallback();
                    _logger.LogWarning(ex, "Sidecar policy response exceeded {TimeoutMs}ms; using cached policy", _warmPathTimeout.TotalMilliseconds);
                }
                catch (TaskCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    _metricsCollector.RecordAiFallback();
                    _logger.LogWarning("Sidecar policy response timed out after {TimeoutMs}ms; using cached policy", _warmPathTimeout.TotalMilliseconds);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Failed to report metrics to sidecar");
                }
            }

            channel?.Dispose();
        }
    }
}
