using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Garnet;
using Garnet.server;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pyrope.GarnetServer.DataModel;
using Pyrope.GarnetServer.Extensions;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Policies;
using Pyrope.GarnetServer.Security;

namespace Pyrope.GarnetServer.Services
{
    public class GarnetService : IHostedService, IDisposable
    {
        private readonly Garnet.GarnetServer _server;
        private readonly ResultCache _resultCache;
        private readonly IPolicyEngine _policyEngine;
        private readonly IMetricsCollector _metricsCollector;
        private readonly LshService _lshService;
        private readonly ITenantQuotaEnforcer _quotaEnforcer;
        private readonly ITenantAuthenticator _tenantAuthenticator;
        private readonly ISloGuardrails _sloGuardrails;
        private readonly SemanticClusterRegistry _clusterRegistry;
        private readonly CanonicalKeyMap _canonicalKeyMap;
        private readonly IPredictivePrefetcher _prefetcher;
        private readonly IPrefetchBackgroundQueue _prefetchQueue;
        private readonly IBillingMeter _billingMeter;
        private readonly ILogger<GarnetService> _logger;

        private volatile bool _disposed;
        private readonly object _disposeLock = new();

        public GarnetService(
            ResultCache resultCache,
            IPolicyEngine policyEngine,
            IMetricsCollector metricsCollector,
            LshService lshService,
            ITenantQuotaEnforcer quotaEnforcer,
            ITenantAuthenticator tenantAuthenticator,
            ISloGuardrails sloGuardrails,
            SemanticClusterRegistry clusterRegistry,
            CanonicalKeyMap canonicalKeyMap,
            IPredictivePrefetcher prefetcher,
            IPrefetchBackgroundQueue prefetchQueue,
            IBillingMeter billingMeter,
            ILogger<GarnetService> logger,
            string[]? args = null)
        {
            ArgumentNullException.ThrowIfNull(resultCache);
            ArgumentNullException.ThrowIfNull(policyEngine);
            ArgumentNullException.ThrowIfNull(metricsCollector);
            ArgumentNullException.ThrowIfNull(lshService);
            ArgumentNullException.ThrowIfNull(quotaEnforcer);
            ArgumentNullException.ThrowIfNull(tenantAuthenticator);
            ArgumentNullException.ThrowIfNull(sloGuardrails);
            ArgumentNullException.ThrowIfNull(clusterRegistry);
            ArgumentNullException.ThrowIfNull(canonicalKeyMap);
            ArgumentNullException.ThrowIfNull(prefetcher);
            ArgumentNullException.ThrowIfNull(prefetchQueue);
            ArgumentNullException.ThrowIfNull(billingMeter);
            ArgumentNullException.ThrowIfNull(logger);

            _resultCache = resultCache;
            _policyEngine = policyEngine;
            _metricsCollector = metricsCollector;
            _lshService = lshService;
            _quotaEnforcer = quotaEnforcer;
            _tenantAuthenticator = tenantAuthenticator;
            _sloGuardrails = sloGuardrails;
            _clusterRegistry = clusterRegistry;
            _canonicalKeyMap = canonicalKeyMap;
            _prefetcher = prefetcher;
            _prefetchQueue = prefetchQueue;
            _billingMeter = billingMeter;
            _logger = logger;

            // Filter out ASP.NET Core specific arguments before passing to Garnet
            var garnetArgs = (args ?? Array.Empty<string>())
                .Where(arg => !arg.StartsWith("--urls", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var authSettings = new Security.PyropeAuthenticationSettings(tenantAuthenticator);
            _server = new Garnet.GarnetServer(garnetArgs, authenticationSettingsOverride: authSettings);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            RegisterCommands();
            try
            {
                _server.Start();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Garnet failed to start: {Message}", ex.Message);
                throw;
            }
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Dispose();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (_disposed) return;
            lock (_disposeLock)
            {
                if (_disposed) return;
                _disposed = true;
                _server?.Dispose();
            }
            GC.SuppressFinalize(this);
        }

        internal VectorCommandSet CreateCommandSet(VectorCommandType commandType)
        {
            return new VectorCommandSet(
                commandType: commandType,
                resultCache: _resultCache,
                policyEngine: _policyEngine,
                metrics: _metricsCollector,
                lshService: _lshService,
                quotaEnforcer: _quotaEnforcer,
                tenantAuthenticator: _tenantAuthenticator,
                sloGuardrails: _sloGuardrails,
                clusterRegistry: _clusterRegistry,
                canonicalKeyMap: _canonicalKeyMap,
                prefetcher: _prefetcher,
                prefetchQueue: _prefetchQueue,
                logger: _logger,
                billingMeter: _billingMeter
            );
        }

        private void RegisterCommands()
        {
            _server.Register.NewCommand("VEC.ADD", CommandType.ReadModifyWrite, CreateCommandSet(VectorCommandType.Add), new RespCommandsInfo { Command = (RespCommand)VectorCommandSet.VEC_ADD, Name = "VEC.ADD" });
            _server.Register.NewCommand("VEC.UPSERT", CommandType.ReadModifyWrite, CreateCommandSet(VectorCommandType.Upsert), new RespCommandsInfo { Command = (RespCommand)VectorCommandSet.VEC_UPSERT, Name = "VEC.UPSERT" });
            _server.Register.NewCommand("VEC.DEL", CommandType.ReadModifyWrite, CreateCommandSet(VectorCommandType.Del), new RespCommandsInfo { Command = (RespCommand)VectorCommandSet.VEC_DEL, Name = "VEC.DEL" });
            _server.Register.NewCommand("VEC.SEARCH", CommandType.Read, CreateCommandSet(VectorCommandType.Search), new RespCommandsInfo { Command = (RespCommand)VectorCommandSet.VEC_SEARCH, Name = "VEC.SEARCH" });
            _server.Register.NewCommand("VEC.STATS", CommandType.Read, CreateCommandSet(VectorCommandType.Stats), new RespCommandsInfo { Command = (RespCommand)VectorCommandSet.VEC_STATS, Name = "VEC.STATS" });
        }
    }
}
