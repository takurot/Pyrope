using System;
using System.Threading;
using Garnet;
using Pyrope.GarnetServer.Extensions;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Policies;
using Pyrope.GarnetServer.Security;
using Microsoft.Extensions.Options;
using Pyrope.GarnetServer.Services;
using StackExchange.Redis;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Extensions
{
    public class VectorSearchCacheTests : IDisposable
    {
        private const string TenantApiKey = "test-tenant-key";
        private readonly TenantRegistry _tenantRegistry = new();
        private readonly ITenantAuthenticator _tenantAuthenticator;
        private readonly Garnet.GarnetServer _server;
        private readonly int _port;
        private readonly MemoryCacheStorage _cacheStorage;

        public VectorSearchCacheTests()
        {
            _port = 4000 + new Random().Next(1000);
            _cacheStorage = new MemoryCacheStorage();
            _tenantAuthenticator = new TenantApiKeyAuthenticator(_tenantRegistry, Options.Create(new ApiKeyAuthOptions()));

            EnsureTenant("t_cache1");
            EnsureTenant("t_cache2");
            EnsureTenant("t_cache3");
            EnsureTenant("t_ttl");

            // Shared components
            var indexRegistry = VectorCommandSet.SharedIndexRegistry;
            var resultCache = new ResultCache(_cacheStorage, indexRegistry);
            var policyEngine = new StaticPolicyEngine(TimeSpan.FromSeconds(1)); // 1s TTL for testing

            try
            {
                _server = new Garnet.GarnetServer(new string[] { "--port", _port.ToString(), "--bind", "127.0.0.1" });

                // Register Write Commands (Standard)
                _server.Register.NewCommand("VEC.ADD", Garnet.server.CommandType.ReadModifyWrite, new VectorCommandSet(VectorCommandType.Add, tenantAuthenticator: _tenantAuthenticator), new Garnet.server.RespCommandsInfo { Command = (Garnet.server.RespCommand)VectorCommandSet.VEC_ADD, Name = "VEC.ADD" });

                // Register Search Command (With Cache)
                _server.Register.NewCommand("VEC.SEARCH", Garnet.server.CommandType.Read, new VectorCommandSet(VectorCommandType.Search, resultCache, policyEngine, tenantAuthenticator: _tenantAuthenticator), new Garnet.server.RespCommandsInfo { Command = (Garnet.server.RespCommand)VectorCommandSet.VEC_SEARCH, Name = "VEC.SEARCH" });

                _server.Start();
            }
            catch
            {
                // Retry once on port conflict
                _port = 4000 + new Random().Next(1000);
                _server = new Garnet.GarnetServer(new string[] { "--port", _port.ToString(), "--bind", "127.0.0.1" });

                _server.Register.NewCommand("VEC.ADD", Garnet.server.CommandType.ReadModifyWrite, new VectorCommandSet(VectorCommandType.Add, tenantAuthenticator: _tenantAuthenticator), new Garnet.server.RespCommandsInfo { Command = (Garnet.server.RespCommand)VectorCommandSet.VEC_ADD, Name = "VEC.ADD" });
                _server.Register.NewCommand("VEC.SEARCH", Garnet.server.CommandType.Read, new VectorCommandSet(VectorCommandType.Search, resultCache, policyEngine, tenantAuthenticator: _tenantAuthenticator), new Garnet.server.RespCommandsInfo { Command = (Garnet.server.RespCommand)VectorCommandSet.VEC_SEARCH, Name = "VEC.SEARCH" });

                _server.Start();
            }
        }

        public void Dispose()
        {
            _server.Dispose();
        }

        [Fact]
        public void Search_ShouldCacheResult_OnFirstCall()
        {
            using var redis = ConnectionMultiplexer.Connect($"127.0.0.1:{_port}");
            var db = redis.GetDatabase();

            // 1. Seed data
            db.Execute("VEC.ADD", "t_cache1", "i_cache1", "d1", "VECTOR", "[1,0]", "API_KEY", TenantApiKey + "-t_cache1");

            // 2. Search (Miss -> Compute -> Cache)
            var result = (RedisResult[]?)db.Execute("VEC.SEARCH", "t_cache1", "i_cache1", "TOPK", "1", "VECTOR", "[1,0]", "API_KEY", TenantApiKey + "-t_cache1");
            Assert.NotNull(result);
            Assert.Single(result!);

            // Check content to ensure it's valid
            var hit = (RedisResult[]?)result![0];
            Assert.Equal("d1", hit![0].ToString());
        }

        [Fact]
        public void Search_ShouldReturnCachedResult_EvenIfDataDeletedFromIndex()
        {
            using var redis = ConnectionMultiplexer.Connect($"127.0.0.1:{_port}");
            var db = redis.GetDatabase();

            // 1. Seed data
            db.Execute("VEC.ADD", "t_cache2", "i_cache2", "d1", "VECTOR", "[1,0]", "API_KEY", TenantApiKey + "-t_cache2");

            // 2. Search (Populate Cache)
            var result1 = (RedisResult[]?)db.Execute("VEC.SEARCH", "t_cache2", "i_cache2", "TOPK", "1", "VECTOR", "[1,0]", "API_KEY", TenantApiKey + "-t_cache2");
            Assert.Equal("d1", ((RedisResult[]?)result1![0])![0].ToString());

            // 3. Delete data (Bypass Garnet Command)
            var registry = VectorCommandSet.SharedIndexRegistry;
            if (registry.TryGetIndex("t_cache2", "i_cache2", out var index))
            {
                index.Delete("d1");
            }

            // 4. Search again
            var result2 = (RedisResult[]?)db.Execute("VEC.SEARCH", "t_cache2", "i_cache2", "TOPK", "1", "VECTOR", "[1,0]", "API_KEY", TenantApiKey + "-t_cache2");

            // If Cached: Should still return "d1".
            Assert.NotNull(result2);
            Assert.Single(result2!);
            Assert.Equal("d1", ((RedisResult[]?)result2![0])![0].ToString());
        }

        [Fact]
        public void Search_ShouldInvalidate_WhenEpochChanges()
        {
            using var redis = ConnectionMultiplexer.Connect($"127.0.0.1:{_port}");
            var db = redis.GetDatabase();

            // 1. Seed
            db.Execute("VEC.ADD", "t_cache3", "i_cache3", "d1", "VECTOR", "[1,0]", "API_KEY", TenantApiKey + "-t_cache3");

            // 2. Cache
            db.Execute("VEC.SEARCH", "t_cache3", "i_cache3", "TOPK", "1", "VECTOR", "[1,0]", "API_KEY", TenantApiKey + "-t_cache3");

            // 3. Add new doc -> Increments Epoch
            db.Execute("VEC.ADD", "t_cache3", "i_cache3", "d2", "VECTOR", "[0,1]", "API_KEY", TenantApiKey + "-t_cache3");

            // 4. Hack: Delete d1 directly from Index (Backdoor)
            var registry = VectorCommandSet.SharedIndexRegistry;
            if (registry.TryGetIndex("t_cache3", "i_cache3", out var index))
            {
                index.Delete("d1");
            }

            // Manually increment epoch to simulate "Some update happened" if VEC.ADD didn't do enough?
            // VEC.ADD already increments epoch. So cache should be invalidated.

            var result = (RedisResult[]?)db.Execute("VEC.SEARCH", "t_cache3", "i_cache3", "TOPK", "5", "VECTOR", "[1,0]", "API_KEY", TenantApiKey + "-t_cache3");

            // Should properly search Index. 
            // Index has d1 deleted, d2 exists.
            // d2 ([0,1]) vs query ([1,0]) -> might be low score but returned if TOPK=5.

            // Verify d1 is NOT present.
            foreach (var r in result!)
            {
                var hit = (RedisResult[]?)r;
                Assert.NotEqual("d1", hit![0].ToString());
            }
        }

        [Fact]
        public void Search_ShouldExpire_AfterTTL()
        {
            using var redis = ConnectionMultiplexer.Connect($"127.0.0.1:{_port}");
            var db = redis.GetDatabase();

            // 1. Seed
            db.Execute("VEC.ADD", "t_ttl", "i_ttl", "d1", "VECTOR", "[1,0]", "API_KEY", TenantApiKey + "-t_ttl");

            // 2. Cache (1s TTL)
            db.Execute("VEC.SEARCH", "t_ttl", "i_ttl", "TOPK", "1", "VECTOR", "[1,0]", "API_KEY", TenantApiKey + "-t_ttl");

            // 3. Backdoor delete
            var registry = VectorCommandSet.SharedIndexRegistry;
            registry.TryGetIndex("t_ttl", "i_ttl", out var index);
            index!.Delete("d1");

            // 4. Immediate check -> Should still be cached (Hit)
            var hitResult = (RedisResult[]?)db.Execute("VEC.SEARCH", "t_ttl", "i_ttl", "TOPK", "1", "VECTOR", "[1,0]", "API_KEY", TenantApiKey + "-t_ttl");
            Assert.Equal("d1", ((RedisResult[]?)hitResult![0])![0].ToString());

            // 5. Wait 1.1s
            Thread.Sleep(1200);

            // 6. Check -> Should be expired (Miss -> Compute -> Empty)
            var missResult = (RedisResult[]?)db.Execute("VEC.SEARCH", "t_ttl", "i_ttl", "TOPK", "1", "VECTOR", "[1,0]", "API_KEY", TenantApiKey + "-t_ttl");
            // Should be empty array
            Assert.Empty(missResult!);
        }

        private void EnsureTenant(string tenantId)
        {
            var apiKey = $"{TenantApiKey}-{tenantId}";
            _tenantRegistry.TryCreate(tenantId, new TenantQuota(), out _, apiKey: apiKey);
        }
    }
}
