using System;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pyrope.Policy;
using Pyrope.GarnetServer.Policies;
using Pyrope.GarnetServer.Security;

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
        private readonly bool _mtlsEnabled;
        private readonly bool _skipServerNameValidation;
        private readonly string? _caCertPemPath;
        private readonly string? _clientCertPemPath;
        private readonly string? _clientKeyPemPath;
        private X509Certificate2? _caCert;

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

            _mtlsEnabled = bool.TryParse(configuration["Sidecar:MtlsEnabled"], out var enabled)
                ? enabled
                : bool.TryParse(Environment.GetEnvironmentVariable("PYROPE_SIDECAR_MTLS_ENABLED"), out var envEnabled) && envEnabled;

            _skipServerNameValidation = bool.TryParse(configuration["Sidecar:MtlsSkipServerNameValidation"], out var skip)
                ? skip
                : bool.TryParse(Environment.GetEnvironmentVariable("PYROPE_SIDECAR_MTLS_SKIP_NAME_VALIDATION"), out var envSkip) && envSkip;

            _caCertPemPath =
                configuration["Sidecar:CaCertPemPath"] ??
                Environment.GetEnvironmentVariable("PYROPE_SIDECAR_CA_CERT_PEM");
            _clientCertPemPath =
                configuration["Sidecar:ClientCertPemPath"] ??
                Environment.GetEnvironmentVariable("PYROPE_SIDECAR_CLIENT_CERT_PEM");
            _clientKeyPemPath =
                configuration["Sidecar:ClientKeyPemPath"] ??
                Environment.GetEnvironmentVariable("PYROPE_SIDECAR_CLIENT_KEY_PEM");

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
                try
                {
                    channel = CreateChannel(_sidecarEndpoint!);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Sidecar metrics reporting disabled (failed to configure gRPC channel)");
                    return;
                }
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

        private GrpcChannel CreateChannel(string endpoint)
        {
            var uri = new Uri(endpoint);
            if (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
            {
                // h2c (plaintext) support for local dev
                AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
                return GrpcChannel.ForAddress(endpoint);
            }

            if (!uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Unsupported sidecar endpoint scheme: {uri.Scheme}");
            }

            var handler = new SocketsHttpHandler
            {
                EnableMultipleHttp2Connections = true
            };

            if (_mtlsEnabled)
            {
                if (string.IsNullOrWhiteSpace(_caCertPemPath) ||
                    string.IsNullOrWhiteSpace(_clientCertPemPath) ||
                    string.IsNullOrWhiteSpace(_clientKeyPemPath))
                {
                    throw new InvalidOperationException("mTLS enabled but cert paths are not configured (CA/client cert/client key).");
                }

                _caCert ??= PemCertificateLoader.LoadCertificateFromPemFile(_caCertPemPath);
                var clientCert = PemCertificateLoader.LoadClientCertificateFromPemFiles(_clientCertPemPath, _clientKeyPemPath);

                handler.SslOptions = new SslClientAuthenticationOptions
                {
                    ClientCertificates = new X509CertificateCollection { clientCert },
                    RemoteCertificateValidationCallback = ValidateServerCertificate
                };
            }

            return GrpcChannel.ForAddress(endpoint, new GrpcChannelOptions { HttpHandler = handler });
        }

        private bool ValidateServerCertificate(
            object? sender,
            X509Certificate? certificate,
            X509Chain? chain,
            SslPolicyErrors sslPolicyErrors)
        {
            if (_caCert == null)
            {
                return sslPolicyErrors == SslPolicyErrors.None;
            }

            if (certificate == null)
            {
                return false;
            }

            // Optionally ignore name mismatch for internal service discovery names in dev.
            if (_skipServerNameValidation)
            {
                sslPolicyErrors &= ~SslPolicyErrors.RemoteCertificateNameMismatch;
            }

            try
            {
                using var customChain = new X509Chain();
                customChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                customChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                customChain.ChainPolicy.CustomTrustStore.Add(_caCert);

                var serverCert = new X509Certificate2(certificate);
                return customChain.Build(serverCert);
            }
            catch
            {
                return false;
            }
        }
    }
}
