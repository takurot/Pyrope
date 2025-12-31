using Garnet;
using Garnet.server;

namespace Pyrope.GarnetServer
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                using var server = new Garnet.GarnetServer(args);
                

                
                var indexRegistry = Pyrope.GarnetServer.Extensions.VectorCommandSet.SharedIndexRegistry;
                var cacheStorage = new Pyrope.GarnetServer.Model.MemoryCacheStorage();
                var metricsCollector = new Pyrope.GarnetServer.Services.MetricsCollector();
                
                var resultCache = new Pyrope.GarnetServer.Model.ResultCache(cacheStorage, indexRegistry, metricsCollector);
                // Default TTL 60 seconds
                var policyEngine = new Pyrope.GarnetServer.Policies.StaticPolicyEngine(TimeSpan.FromSeconds(60));

                // Register Custom Commands
                server.Register.NewCommand("VEC.ADD", Garnet.server.CommandType.ReadModifyWrite, new Pyrope.GarnetServer.Extensions.VectorCommandSet(Pyrope.GarnetServer.Extensions.VectorCommandType.Add), new Garnet.server.RespCommandsInfo { Command = (Garnet.server.RespCommand)Pyrope.GarnetServer.Extensions.VectorCommandSet.VEC_ADD, Name = "VEC.ADD" });
                server.Register.NewCommand("VEC.UPSERT", Garnet.server.CommandType.ReadModifyWrite, new Pyrope.GarnetServer.Extensions.VectorCommandSet(Pyrope.GarnetServer.Extensions.VectorCommandType.Upsert), new Garnet.server.RespCommandsInfo { Command = (Garnet.server.RespCommand)Pyrope.GarnetServer.Extensions.VectorCommandSet.VEC_UPSERT, Name = "VEC.UPSERT" });
                server.Register.NewCommand("VEC.DEL", Garnet.server.CommandType.ReadModifyWrite, new Pyrope.GarnetServer.Extensions.VectorCommandSet(Pyrope.GarnetServer.Extensions.VectorCommandType.Del), new Garnet.server.RespCommandsInfo { Command = (Garnet.server.RespCommand)Pyrope.GarnetServer.Extensions.VectorCommandSet.VEC_DEL, Name = "VEC.DEL" });
                
                // VEC.SEARCH with Caching & Policy & Metrics
                server.Register.NewCommand("VEC.SEARCH", Garnet.server.CommandType.Read, new Pyrope.GarnetServer.Extensions.VectorCommandSet(Pyrope.GarnetServer.Extensions.VectorCommandType.Search, resultCache, policyEngine, metricsCollector), new Garnet.server.RespCommandsInfo { Command = (Garnet.server.RespCommand)Pyrope.GarnetServer.Extensions.VectorCommandSet.VEC_SEARCH, Name = "VEC.SEARCH" });

                // VEC.STATS
                server.Register.NewCommand("VEC.STATS", Garnet.server.CommandType.Read, new Pyrope.GarnetServer.Extensions.VectorCommandSet(Pyrope.GarnetServer.Extensions.VectorCommandType.Stats, null, null, metricsCollector), new Garnet.server.RespCommandsInfo { Command = (Garnet.server.RespCommand)Pyrope.GarnetServer.Extensions.VectorCommandSet.VEC_STATS, Name = "VEC.STATS" });
                server.Start();
                Thread.Sleep(Timeout.Infinite);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unable to start Garnet server: {ex.Message}");
            }
        }
    }
}
