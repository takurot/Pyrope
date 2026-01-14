using Microsoft.Extensions.Hosting;
using Garnet;
using Garnet.server;
using Pyrope.GarnetServer.Extensions;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Policies;
using Pyrope.GarnetServer.Security;
using Pyrope.GarnetServer.DataModel;

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
        private readonly ILogger<GarnetService> _logger;

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
            ILogger<GarnetService> logger,
            string[]? args = null)
        {
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
            _logger = logger;
            // Filter out ASP.NET Core specific arguments before passing to Garnet
            var garnetArgs = (args ?? Array.Empty<string>())
                .Where(arg => !arg.StartsWith("--urls", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            _server = new Garnet.GarnetServer(garnetArgs);
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
                Console.WriteLine($"[Warning] Garnet failed to start: {ex.Message}");
            }
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _server.Dispose();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _server.Dispose();
        }

        private void RegisterCommands()
        {
            // Register Custom Commands
            _server.Register.NewCommand("VEC.ADD", CommandType.ReadModifyWrite, new VectorCommandSet(VectorCommandType.Add, null, null, null, null, _quotaEnforcer, _tenantAuthenticator), new RespCommandsInfo { Command = (RespCommand)VectorCommandSet.VEC_ADD, Name = "VEC.ADD" });
            _server.Register.NewCommand("VEC.UPSERT", CommandType.ReadModifyWrite, new VectorCommandSet(VectorCommandType.Upsert, null, null, null, null, _quotaEnforcer, _tenantAuthenticator), new RespCommandsInfo { Command = (RespCommand)VectorCommandSet.VEC_UPSERT, Name = "VEC.UPSERT" });
            _server.Register.NewCommand("VEC.DEL", CommandType.ReadModifyWrite, new VectorCommandSet(VectorCommandType.Del, null, null, null, null, _quotaEnforcer, _tenantAuthenticator), new RespCommandsInfo { Command = (RespCommand)VectorCommandSet.VEC_DEL, Name = "VEC.DEL" });

            // VEC.SEARCH with Caching & Policy & Metrics & LSH & Semantic Clustering & Canonical Aliasing
            _server.Register.NewCommand("VEC.SEARCH", CommandType.Read, new VectorCommandSet(VectorCommandType.Search, _resultCache, _policyEngine, _metricsCollector, _lshService, _quotaEnforcer, _tenantAuthenticator, _sloGuardrails, _clusterRegistry, _canonicalKeyMap, _prefetcher, _prefetchQueue, _logger), new RespCommandsInfo { Command = (RespCommand)VectorCommandSet.VEC_SEARCH, Name = "VEC.SEARCH" });

            // VEC.STATS
            _server.Register.NewCommand("VEC.STATS", CommandType.Read, new VectorCommandSet(VectorCommandType.Stats, null, null, _metricsCollector, null, null, _tenantAuthenticator), new RespCommandsInfo { Command = (RespCommand)VectorCommandSet.VEC_STATS, Name = "VEC.STATS" });
        }
    }
}
