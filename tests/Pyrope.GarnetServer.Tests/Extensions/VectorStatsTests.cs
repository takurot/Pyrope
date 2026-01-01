using System;
using System.Threading;
using Garnet;
using Pyrope.GarnetServer.Extensions;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Policies;
using Pyrope.GarnetServer.Services;
using StackExchange.Redis;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Extensions
{
    public class VectorStatsTests : IDisposable
    {
        private readonly Garnet.GarnetServer _server;
        private readonly int _port;

        public VectorStatsTests()
        {
            _port = 5000 + new Random().Next(1000);

            // Shared components
            var metrics = new MetricsCollector();
            var indexRegistry = VectorCommandSet.SharedIndexRegistry;
            var cacheStorage = new MemoryCacheStorage();
            var resultCache = new ResultCache(cacheStorage, indexRegistry, metrics);
            var policyEngine = new StaticPolicyEngine(TimeSpan.FromSeconds(60));


            try
            {
                _server = new Garnet.GarnetServer(new string[] { "--port", _port.ToString(), "--bind", "127.0.0.1" });

                _server.Register.NewCommand("VEC.ADD", Garnet.server.CommandType.ReadModifyWrite, new VectorCommandSet(VectorCommandType.Add), new Garnet.server.RespCommandsInfo { Command = (Garnet.server.RespCommand)VectorCommandSet.VEC_ADD, Name = "VEC.ADD" });
                _server.Register.NewCommand("VEC.SEARCH", Garnet.server.CommandType.Read, new VectorCommandSet(VectorCommandType.Search, resultCache, policyEngine, metrics), new Garnet.server.RespCommandsInfo { Command = (Garnet.server.RespCommand)VectorCommandSet.VEC_SEARCH, Name = "VEC.SEARCH" });
                _server.Register.NewCommand("VEC.STATS", Garnet.server.CommandType.Read, new VectorCommandSet(VectorCommandType.Stats, null, null, metrics), new Garnet.server.RespCommandsInfo { Command = (Garnet.server.RespCommand)VectorCommandSet.VEC_STATS, Name = "VEC.STATS" });

                _server.Start();
            }
            catch
            {
                // Retry logic omitted for brevity in test, assumption is low conflict risk
                throw;
            }
        }

        public void Dispose()
        {
            _server.Dispose();
        }

        [Fact]
        public void VecStats_ShouldReturnMetrics()
        {
            using var redis = ConnectionMultiplexer.Connect($"127.0.0.1:{_port}");
            var db = redis.GetDatabase();

            // 1. Initial State
            db.Execute("SET", "sys:dummy", "1");
            var initialStats = (string?)db.Execute("VEC.STATS", "sys:dummy");
            Assert.Contains("cache_hit_total 0", initialStats);
            Assert.Contains("cache_miss_total 0", initialStats);

            // 2. Perform Search (Miss)
            db.Execute("VEC.ADD", "t_stats", "i_stats", "d1", "VECTOR", "[1,0]");
            db.Execute("VEC.SEARCH", "t_stats", "i_stats", "TOPK", "1", "VECTOR", "[1,0]");

            var missStats = (string?)db.Execute("VEC.STATS", "sys:dummy");
            Assert.Contains("cache_miss_total 1", missStats);
            Assert.Contains("cache_hit_total 0", missStats);
            Assert.Contains("vector_search_latency_ms_bucket{le=\"+Inf\"} 1", missStats);

            // 3. Perform Search (Hit)
            db.Execute("VEC.SEARCH", "t_stats", "i_stats", "TOPK", "1", "VECTOR", "[1,0]");

            var hitStats = (string?)db.Execute("VEC.STATS", "sys:dummy");
            Assert.Contains("cache_miss_total 1", hitStats);
            Assert.Contains("cache_hit_total 1", hitStats);
            Assert.Contains("vector_search_latency_ms_bucket{le=\"+Inf\"} 2", hitStats);
        }
    }
}
