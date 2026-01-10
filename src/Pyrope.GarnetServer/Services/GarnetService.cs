using Microsoft.Extensions.Hosting;
using Garnet;
using Garnet.server;
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

        public GarnetService(
            ResultCache resultCache,
            IPolicyEngine policyEngine,
            IMetricsCollector metricsCollector,
            LshService lshService,
            ITenantQuotaEnforcer quotaEnforcer,
            ITenantAuthenticator tenantAuthenticator,
            ISloGuardrails sloGuardrails,
            string[]? args = null)
        {
            _resultCache = resultCache;
            _policyEngine = policyEngine;
            _metricsCollector = metricsCollector;
            _lshService = lshService;
            _quotaEnforcer = quotaEnforcer;
            _tenantAuthenticator = tenantAuthenticator;
            _sloGuardrails = sloGuardrails;
            _server = new Garnet.GarnetServer(args ?? Array.Empty<string>());
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

            // VEC.SEARCH with Caching & Policy & Metrics & LSH
            _server.Register.NewCommand("VEC.SEARCH", CommandType.Read, new VectorCommandSet(VectorCommandType.Search, _resultCache, _policyEngine, _metricsCollector, _lshService, _quotaEnforcer, _tenantAuthenticator, _sloGuardrails), new RespCommandsInfo { Command = (RespCommand)VectorCommandSet.VEC_SEARCH, Name = "VEC.SEARCH" });

            // VEC.STATS
            _server.Register.NewCommand("VEC.STATS", CommandType.Read, new VectorCommandSet(VectorCommandType.Stats, null, null, _metricsCollector, null, null, _tenantAuthenticator), new RespCommandsInfo { Command = (RespCommand)VectorCommandSet.VEC_STATS, Name = "VEC.STATS" });
        }
    }
}
