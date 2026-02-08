using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Pyrope.GarnetServer.Policies;
using Pyrope.GarnetServer.Services;
using Pyrope.Policy;
using Xunit;
using Xunit.Sdk;

namespace Pyrope.GarnetServer.Tests.Services
{
    public class SidecarMetricsReporterTests
    {
        [Fact]
        public async Task ReportSystemMetrics_Timeout_IncrementsFallback()
        {
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new[]
                {
                    new KeyValuePair<string, string?>("Sidecar:Endpoint", "http://localhost:50051"),
                    new KeyValuePair<string, string?>("Sidecar:MetricsIntervalSeconds", "1"),
                    new KeyValuePair<string, string?>("Sidecar:WarmPathTimeoutMs", "10")
                })
                .Build();

            var metrics = new MetricsCollector();
            var systemUsageProvider = new TestSystemUsageProvider();
            var billingMeter = new BillingMeter(new BillingOptions { CostUnitSeconds = 1.0 });
            var policyEngine = new StaticPolicyEngine(TimeSpan.FromSeconds(1));
            var slowClient = new SlowPolicyServiceClient();
            var reporter = new SidecarMetricsReporter(
                metrics,
                systemUsageProvider,
                billingMeter,
                policyEngine,
                configuration,
                NullLogger<SidecarMetricsReporter>.Instance,
                new MockServiceProvider(slowClient));

            await reporter.StartAsync(CancellationToken.None);

            await WaitForFallbackAsync(metrics, TimeSpan.FromSeconds(3));

            await reporter.StopAsync(CancellationToken.None);

            Assert.True(metrics.GetSnapshot().AiFallbacks > 0);
        }

        [Fact]
        public async Task ReportSystemMetrics_SendsTenantIdMetadata()
        {
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new[]
                {
                    new KeyValuePair<string, string?>("Sidecar:Endpoint", "http://localhost:50051"),
                    new KeyValuePair<string, string?>("Sidecar:MetricsIntervalSeconds", "1"),
                    new KeyValuePair<string, string?>("Sidecar:WarmPathTimeoutMs", "200")
                })
                .Build();

            var metrics = new MetricsCollector();
            var systemUsageProvider = new TestSystemUsageProvider();
            var billingMeter = new BillingMeter(new BillingOptions { CostUnitSeconds = 1.0 });
            var policyEngine = new StaticPolicyEngine(TimeSpan.FromSeconds(1));
            var client = new CapturingPolicyServiceClient();
            var reporter = new SidecarMetricsReporter(
                metrics,
                systemUsageProvider,
                billingMeter,
                policyEngine,
                configuration,
                NullLogger<SidecarMetricsReporter>.Instance,
                new MockServiceProvider(client));

            // Generate tenant usage so reporter emits tenant-scoped metrics.
            billingMeter.RecordRequest("tenant-a", cacheHit: true);
            billingMeter.RecordRequest("tenant-a", cacheHit: false);
            metrics.RecordSearchLatency(TimeSpan.FromMilliseconds(8));

            await reporter.StartAsync(CancellationToken.None);

            await WaitForConditionAsync(
                () => client.LastTenantId == "tenant-a",
                TimeSpan.FromSeconds(3),
                "Timed out waiting for tenant metadata.");

            await reporter.StopAsync(CancellationToken.None);

            Assert.Equal("tenant-a", client.LastTenantId);
        }

        private static async Task WaitForFallbackAsync(MetricsCollector metrics, TimeSpan timeout)
        {
            var deadline = DateTimeOffset.UtcNow + timeout;
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (metrics.GetSnapshot().AiFallbacks > 0)
                {
                    return;
                }

                await Task.Delay(50);
            }

            throw new XunitException("Timed out waiting for ai_fallback_total to increment.");
        }

        private static async Task WaitForConditionAsync(Func<bool> predicate, TimeSpan timeout, string message)
        {
            var deadline = DateTimeOffset.UtcNow + timeout;
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (predicate())
                {
                    return;
                }

                await Task.Delay(50);
            }

            throw new XunitException(message);
        }

        private sealed class TestSystemUsageProvider : ISystemUsageProvider
        {
            private long _sequence;
            public int ProcessorCount { get; } = 1;

            public SystemUsageSnapshot GetSnapshot()
            {
                var seq = Interlocked.Increment(ref _sequence);
                return new SystemUsageSnapshot(
                    DateTimeOffset.UtcNow.AddMilliseconds(seq * 100),
                    TimeSpan.FromMilliseconds(seq * 10));
            }
        }

        private sealed class SlowPolicyServiceClient : PolicyService.PolicyServiceClient
        {
            public override AsyncUnaryCall<SystemMetricsResponse> ReportSystemMetricsAsync(
                SystemMetricsRequest request,
                Metadata? headers = null,
                DateTime? deadline = null,
                CancellationToken cancellationToken = default)
            {
                var responseTask = SimulateTimeoutAsync(deadline, cancellationToken);
                return new AsyncUnaryCall<SystemMetricsResponse>(
                    responseTask,
                    Task.FromResult(new Metadata()),
                    () => Status.DefaultSuccess,
                    () => new Metadata(),
                    () => { });
            }

            private static async Task<SystemMetricsResponse> SimulateTimeoutAsync(
                DateTime? deadline,
                CancellationToken cancellationToken)
            {
                if (deadline.HasValue)
                {
                    var timeout = deadline.Value - DateTime.UtcNow;
                    if (timeout <= TimeSpan.Zero)
                    {
                        throw new RpcException(new Status(StatusCode.DeadlineExceeded, "deadline"));
                    }

                    await Task.Delay(timeout, cancellationToken);
                    throw new RpcException(new Status(StatusCode.DeadlineExceeded, "deadline"));
                }

                await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
                return new SystemMetricsResponse { Status = "OK" };
            }
        }

        private sealed class CapturingPolicyServiceClient : PolicyService.PolicyServiceClient
        {
            private string? _lastTenantId;
            public string? LastTenantId => _lastTenantId;

            public override AsyncUnaryCall<SystemMetricsResponse> ReportSystemMetricsAsync(
                SystemMetricsRequest request,
                Metadata? headers = null,
                DateTime? deadline = null,
                CancellationToken cancellationToken = default)
            {
                _lastTenantId = null;
                if (headers != null)
                {
                    foreach (var entry in headers)
                    {
                        if (string.Equals(entry.Key, "tenant-id", StringComparison.OrdinalIgnoreCase))
                        {
                            _lastTenantId = entry.Value;
                            break;
                        }
                    }
                }
                return new AsyncUnaryCall<SystemMetricsResponse>(
                    Task.FromResult(new SystemMetricsResponse
                    {
                        Status = "OK",
                        NextReportIntervalMs = 1000,
                        Policy = new WarmPathPolicy
                        {
                            AdmissionThreshold = 0.5,
                            TtlSeconds = 60,
                            EvictionPriority = 1
                        }
                    }),
                    Task.FromResult(new Metadata()),
                    () => Status.DefaultSuccess,
                    () => new Metadata(),
                    () => { });
            }
        }


        private sealed class MockServiceProvider : IServiceProvider
        {
            private readonly object _service;
            public MockServiceProvider(object service) => _service = service;
            public object? GetService(Type serviceType) => _service;
        }
    }
}
