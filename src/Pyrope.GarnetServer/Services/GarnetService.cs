using Microsoft.Extensions.Hosting;
using Garnet;
using Garnet.server;
using Pyrope.GarnetServer.Extensions;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Policies;

namespace Pyrope.GarnetServer.Services
{
    public class GarnetService : IHostedService, IDisposable
    {
        private readonly Garnet.GarnetServer _server;
        private readonly ResultCache _resultCache;
        private readonly IPolicyEngine _policyEngine;
        private readonly IMetricsCollector _metricsCollector;
        private readonly LshService _lshService;

        public GarnetService(
            ResultCache resultCache,
            IPolicyEngine policyEngine,
            IMetricsCollector metricsCollector,
            LshService lshService,
            string[]? args = null)
        {
            _resultCache = resultCache;
            _policyEngine = policyEngine;
            _metricsCollector = metricsCollector;
            _lshService = lshService;
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
            _server.Register.NewCommand("VEC.ADD", CommandType.ReadModifyWrite, new VectorCommandSet(VectorCommandType.Add), new RespCommandsInfo { Command = (RespCommand)VectorCommandSet.VEC_ADD, Name = "VEC.ADD" });
            _server.Register.NewCommand("VEC.UPSERT", CommandType.ReadModifyWrite, new VectorCommandSet(VectorCommandType.Upsert), new RespCommandsInfo { Command = (RespCommand)VectorCommandSet.VEC_UPSERT, Name = "VEC.UPSERT" });
            _server.Register.NewCommand("VEC.DEL", CommandType.ReadModifyWrite, new VectorCommandSet(VectorCommandType.Del), new RespCommandsInfo { Command = (RespCommand)VectorCommandSet.VEC_DEL, Name = "VEC.DEL" });
            
            // VEC.SEARCH with Caching & Policy & Metrics & LSH
            _server.Register.NewCommand("VEC.SEARCH", CommandType.Read, new VectorCommandSet(VectorCommandType.Search, _resultCache, _policyEngine, _metricsCollector, _lshService), new RespCommandsInfo { Command = (RespCommand)VectorCommandSet.VEC_SEARCH, Name = "VEC.SEARCH" });

            // VEC.STATS
            _server.Register.NewCommand("VEC.STATS", CommandType.Read, new VectorCommandSet(VectorCommandType.Stats, null, null, _metricsCollector), new RespCommandsInfo { Command = (RespCommand)VectorCommandSet.VEC_STATS, Name = "VEC.STATS" });
        }
    }
}
