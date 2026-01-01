using System.Text.Json;
using System.Text;
using StackExchange.Redis;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Extensions
{
    public class SemanticCacheTests : IDisposable
    {
        private readonly Garnet.GarnetServer _server;
        private readonly ConnectionMultiplexer _redis;
        private readonly IDatabase _db;

        public SemanticCacheTests()
        {
            var port = 3278 + new Random().Next(1000); // Random port to avoid collision
            _server = new Garnet.GarnetServer(new string[] { "--port", port.ToString(), "--bind", "127.0.0.1" });

            // Manual registration to include LSH
            var indexRegistry = new Pyrope.GarnetServer.Services.VectorIndexRegistry();
            var cacheStorage = new Pyrope.GarnetServer.Model.MemoryCacheStorage();
            var metrics = new Pyrope.GarnetServer.Services.MetricsCollector();
            var lsh = new Pyrope.GarnetServer.Services.LshService(seed: 42); // Deterministic
            var resultCache = new Pyrope.GarnetServer.Model.ResultCache(cacheStorage, indexRegistry, metrics);
            // Always cache policy
            var policy = new Pyrope.GarnetServer.Policies.StaticPolicyEngine(TimeSpan.FromMinutes(5));

            _server.Register.NewCommand("VEC.ADD", Garnet.server.CommandType.ReadModifyWrite, new Pyrope.GarnetServer.Extensions.VectorCommandSet(Pyrope.GarnetServer.Extensions.VectorCommandType.Add), new Garnet.server.RespCommandsInfo { Command = (Garnet.server.RespCommand)Pyrope.GarnetServer.Extensions.VectorCommandSet.VEC_ADD, Name = "VEC.ADD" });
            _server.Register.NewCommand("VEC.SEARCH", Garnet.server.CommandType.Read, new Pyrope.GarnetServer.Extensions.VectorCommandSet(Pyrope.GarnetServer.Extensions.VectorCommandType.Search, resultCache, policy, metrics, lsh), new Garnet.server.RespCommandsInfo { Command = (Garnet.server.RespCommand)Pyrope.GarnetServer.Extensions.VectorCommandSet.VEC_SEARCH, Name = "VEC.SEARCH" });
            _server.Register.NewCommand("VEC.STATS", Garnet.server.CommandType.Read, new Pyrope.GarnetServer.Extensions.VectorCommandSet(Pyrope.GarnetServer.Extensions.VectorCommandType.Stats, null, null, metrics), new Garnet.server.RespCommandsInfo { Command = (Garnet.server.RespCommand)Pyrope.GarnetServer.Extensions.VectorCommandSet.VEC_STATS, Name = "VEC.STATS" });

            _server.Start();
            _redis = ConnectionMultiplexer.Connect($"127.0.0.1:{port},allowAdmin=true");
            _db = _redis.GetDatabase();
        }

        public void Dispose()
        {
            _redis.Dispose();
            _server.Dispose();
        }

        [Fact]
        public void Search_SimilarVector_ShouldHitCache()
        {
            // 1. Add Data
            var tenant = "tenant_sem";
            var index = "idx_sem";
            var id = "item1";
            var vector = new float[4] { 1, 0, 0, 0 }; // 4 dims

            // Syntax: VEC.ADD tenant index id VECTOR [1,0,0,0]
            _db.Execute("VEC.ADD", tenant, index, id, "VECTOR", JsonSerializer.Serialize(vector));

            // 2. Search Original (L0 Miss -> Populates L0 & L1)
            // Query: [1, 0, 0, 0] -> Exact match to data
            var q1 = new float[4] { 1, 0, 0, 0 };
            // Syntax: VEC.SEARCH tenant index TOPK 5 VECTOR [...]
            _db.Execute("VEC.SEARCH", tenant, index, "TOPK", "5", "VECTOR", JsonSerializer.Serialize(q1));

            // Ensure dummy key exists for stats command
            _db.StringSet("sys:dummy", "init");

            // Check Stats: 1 Miss
            var stats1 = (string?)_db.Execute("VEC.STATS", "sys:dummy");
            Assert.NotNull(stats1);
            Assert.Contains("cache_miss_total 1", stats1!);

            // 3. Search Similar (SimHash match expected)
            // Query: [2, 0, 0, 0] -> Same angle as [1, 0, 0, 0] (SimHash identical), but L0 Miss.
            var q2 = new float[4] { 2, 0, 0, 0 };
            _db.Execute("VEC.SEARCH", tenant, index, "TOPK", "5", "VECTOR", JsonSerializer.Serialize(q2));

            // Check Stats: 1 Hit (L1)
            var stats2 = (string?)_db.Execute("VEC.STATS", "sys:dummy");
            Assert.NotNull(stats2);
            Assert.Contains("cache_hit_total 1", stats2!);
        }
    }
}
