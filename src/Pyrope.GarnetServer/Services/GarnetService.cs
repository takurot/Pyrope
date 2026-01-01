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
            LshService lshService)
        {
            _resultCache = resultCache;
            _policyEngine = policyEngine;
            _metricsCollector = metricsCollector;
            _lshService = lshService;
            
            // We need args for GarnetServer, but passing them via DI is tricky.
            // For now, we assume simple args or empty.
            // If we needed args from Main, we'd need to register them.
            // Let's rely on default args for now, or read from Environment.
            // Assuming no args for simplicity in this refactor step, 
            // as standard Garnet args are usually handled by console Main.
            // But we can workaround this by registering string[] args in DI.
            // I'll assume empty args here and see if it works, or inject args if registered.
             _server = new Garnet.GarnetServer(Array.Empty<string>()); 
        }
        
        // Constructor that accepts args if registered
        public GarnetService(
            ResultCache resultCache,
            IPolicyEngine policyEngine,
            IMetricsCollector metricsCollector,
            LshService lshService,
            string[] args)
        {
             _resultCache = resultCache;
            _policyEngine = policyEngine;
            _metricsCollector = metricsCollector;
            _lshService = lshService;
            _server = new Garnet.GarnetServer(args);
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
