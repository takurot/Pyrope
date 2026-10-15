using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pyrope.GarnetServer.DataModel;
using Pyrope.GarnetServer.Extensions;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Policies;
using Pyrope.GarnetServer.Security;
using Pyrope.GarnetServer.Services;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Services
{
    public class GarnetServiceTests
    {
        private static (
            ResultCache ResultCache,
            IPolicyEngine PolicyEngine,
            IMetricsCollector MetricsCollector,
            LshService LshService,
            ITenantQuotaEnforcer QuotaEnforcer,
            ITenantAuthenticator TenantAuthenticator,
            ISloGuardrails SloGuardrails,
            SemanticClusterRegistry ClusterRegistry,
            CanonicalKeyMap CanonicalKeyMap,
            IPredictivePrefetcher Prefetcher,
            IPrefetchBackgroundQueue PrefetchQueue,
            IBillingMeter BillingMeter,
            ILogger<GarnetService> Logger) CreateDependencies()
        {
            var storageMock = new Mock<ICacheStorage>();
            var indexRegistry = new VectorIndexRegistry();
            var resultCache = new ResultCache(storageMock.Object, indexRegistry);
            var policyEngine = new Mock<IPolicyEngine>().Object;
            var metricsCollector = new Mock<IMetricsCollector>().Object;
            var lshService = new LshService();
            var quotaEnforcer = new Mock<ITenantQuotaEnforcer>().Object;
            var tenantAuthenticator = new Mock<ITenantAuthenticator>().Object;
            var sloGuardrails = new Mock<ISloGuardrails>().Object;
            var clusterRegistry = new SemanticClusterRegistry();
            var canonicalKeyMap = new CanonicalKeyMap();
            var prefetcher = new Mock<IPredictivePrefetcher>().Object;
            var prefetchQueue = new Mock<IPrefetchBackgroundQueue>().Object;
            var billingMeter = new Mock<IBillingMeter>().Object;
            var logger = NullLogger<GarnetService>.Instance;

            return (
                resultCache,
                policyEngine,
                metricsCollector,
                lshService,
                quotaEnforcer,
                tenantAuthenticator,
                sloGuardrails,
                clusterRegistry,
                canonicalKeyMap,
                prefetcher,
                prefetchQueue,
                billingMeter,
                logger);
        }

        private static GarnetService CreateService(string[]? args = null)
        {
            var d = CreateDependencies();
            return new GarnetService(
                d.ResultCache,
                d.PolicyEngine,
                d.MetricsCollector,
                d.LshService,
                d.QuotaEnforcer,
                d.TenantAuthenticator,
                d.SloGuardrails,
                d.ClusterRegistry,
                d.CanonicalKeyMap,
                d.Prefetcher,
                d.PrefetchQueue,
                d.BillingMeter,
                d.Logger,
                args);
        }

        [Fact]
        public void Constructor_WithNullDependencies_ThrowsArgumentNullException()
        {
            var d = CreateDependencies();

            Assert.Throws<ArgumentNullException>(() => new GarnetService(
                null!, d.PolicyEngine, d.MetricsCollector, d.LshService, d.QuotaEnforcer, d.TenantAuthenticator,
                d.SloGuardrails, d.ClusterRegistry, d.CanonicalKeyMap, d.Prefetcher, d.PrefetchQueue, d.BillingMeter, d.Logger));

            Assert.Throws<ArgumentNullException>(() => new GarnetService(
                d.ResultCache, null!, d.MetricsCollector, d.LshService, d.QuotaEnforcer, d.TenantAuthenticator,
                d.SloGuardrails, d.ClusterRegistry, d.CanonicalKeyMap, d.Prefetcher, d.PrefetchQueue, d.BillingMeter, d.Logger));

            Assert.Throws<ArgumentNullException>(() => new GarnetService(
                d.ResultCache, d.PolicyEngine, null!, d.LshService, d.QuotaEnforcer, d.TenantAuthenticator,
                d.SloGuardrails, d.ClusterRegistry, d.CanonicalKeyMap, d.Prefetcher, d.PrefetchQueue, d.BillingMeter, d.Logger));

            Assert.Throws<ArgumentNullException>(() => new GarnetService(
                d.ResultCache, d.PolicyEngine, d.MetricsCollector, null!, d.QuotaEnforcer, d.TenantAuthenticator,
                d.SloGuardrails, d.ClusterRegistry, d.CanonicalKeyMap, d.Prefetcher, d.PrefetchQueue, d.BillingMeter, d.Logger));

            Assert.Throws<ArgumentNullException>(() => new GarnetService(
                d.ResultCache, d.PolicyEngine, d.MetricsCollector, d.LshService, null!, d.TenantAuthenticator,
                d.SloGuardrails, d.ClusterRegistry, d.CanonicalKeyMap, d.Prefetcher, d.PrefetchQueue, d.BillingMeter, d.Logger));

            Assert.Throws<ArgumentNullException>(() => new GarnetService(
                d.ResultCache, d.PolicyEngine, d.MetricsCollector, d.LshService, d.QuotaEnforcer, null!,
                d.SloGuardrails, d.ClusterRegistry, d.CanonicalKeyMap, d.Prefetcher, d.PrefetchQueue, d.BillingMeter, d.Logger));

            Assert.Throws<ArgumentNullException>(() => new GarnetService(
                d.ResultCache, d.PolicyEngine, d.MetricsCollector, d.LshService, d.QuotaEnforcer, d.TenantAuthenticator,
                null!, d.ClusterRegistry, d.CanonicalKeyMap, d.Prefetcher, d.PrefetchQueue, d.BillingMeter, d.Logger));

            Assert.Throws<ArgumentNullException>(() => new GarnetService(
                d.ResultCache, d.PolicyEngine, d.MetricsCollector, d.LshService, d.QuotaEnforcer, d.TenantAuthenticator,
                d.SloGuardrails, null!, d.CanonicalKeyMap, d.Prefetcher, d.PrefetchQueue, d.BillingMeter, d.Logger));

            Assert.Throws<ArgumentNullException>(() => new GarnetService(
                d.ResultCache, d.PolicyEngine, d.MetricsCollector, d.LshService, d.QuotaEnforcer, d.TenantAuthenticator,
                d.SloGuardrails, d.ClusterRegistry, null!, d.Prefetcher, d.PrefetchQueue, d.BillingMeter, d.Logger));

            Assert.Throws<ArgumentNullException>(() => new GarnetService(
                d.ResultCache, d.PolicyEngine, d.MetricsCollector, d.LshService, d.QuotaEnforcer, d.TenantAuthenticator,
                d.SloGuardrails, d.ClusterRegistry, d.CanonicalKeyMap, null!, d.PrefetchQueue, d.BillingMeter, d.Logger));

            Assert.Throws<ArgumentNullException>(() => new GarnetService(
                d.ResultCache, d.PolicyEngine, d.MetricsCollector, d.LshService, d.QuotaEnforcer, d.TenantAuthenticator,
                d.SloGuardrails, d.ClusterRegistry, d.CanonicalKeyMap, d.Prefetcher, null!, d.BillingMeter, d.Logger));

            Assert.Throws<ArgumentNullException>(() => new GarnetService(
                d.ResultCache, d.PolicyEngine, d.MetricsCollector, d.LshService, d.QuotaEnforcer, d.TenantAuthenticator,
                d.SloGuardrails, d.ClusterRegistry, d.CanonicalKeyMap, d.Prefetcher, d.PrefetchQueue, null!, d.Logger));

            Assert.Throws<ArgumentNullException>(() => new GarnetService(
                d.ResultCache, d.PolicyEngine, d.MetricsCollector, d.LshService, d.QuotaEnforcer, d.TenantAuthenticator,
                d.SloGuardrails, d.ClusterRegistry, d.CanonicalKeyMap, d.Prefetcher, d.PrefetchQueue, d.BillingMeter, null!));
        }

        [Fact]
        public async Task StartAsync_WhenPortConflictOccurs_RethrowsException()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int boundPort = ((IPEndPoint)listener.LocalEndpoint).Port;

            try
            {
                using var service = CreateService(new[]
                {
                    "--port", boundPort.ToString(),
                    "--bind", "127.0.0.1"
                });

                // Must rethrow startup exception rather than swallowing
                await Assert.ThrowsAnyAsync<Exception>(() => service.StartAsync(CancellationToken.None));
            }
            finally
            {
                listener.Stop();
            }
        }

        [Fact]
        public async Task Dispose_AndStopAsync_AreIdempotentWithoutDoubleDispose()
        {
            using var service = CreateService(new[] { "--port", "0" });

            await service.StopAsync(CancellationToken.None);
            service.Dispose();
            service.Dispose();
        }

        [Theory]
        [InlineData(VectorCommandType.Add)]
        [InlineData(VectorCommandType.Upsert)]
        [InlineData(VectorCommandType.Del)]
        [InlineData(VectorCommandType.Search)]
        [InlineData(VectorCommandType.Stats)]
        public void CreateCommandSet_PassesAllRequiredDependencies(VectorCommandType commandType)
        {
            using var service = CreateService(new[] { "--port", "0" });
            var commandSet = service.CreateCommandSet(commandType);

            Assert.NotNull(commandSet.ClusterRegistry);
            Assert.NotNull(commandSet.Meter);
            Assert.NotNull(commandSet.Metrics);
            Assert.NotNull(commandSet.Logger);
        }
    }
}
