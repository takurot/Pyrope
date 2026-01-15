using System;
using Garnet;
using Pyrope.GarnetServer.Extensions;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Policies;
using Pyrope.GarnetServer.Security;
using Pyrope.GarnetServer.Services;
using StackExchange.Redis;
using Xunit;

using Microsoft.Extensions.Options;

namespace Pyrope.GarnetServer.Tests.Extensions
{
    public sealed class CacheHintForceTests : IDisposable
    {
        private const string TenantApiKey = "test-tenant-key";
        private readonly TenantRegistry _tenantRegistry = new();
        private readonly ITenantAuthenticator _tenantAuthenticator;
        private readonly Garnet.GarnetServer _server;
        private readonly int _port;

        public CacheHintForceTests()
        {
            _tenantAuthenticator = new TenantApiKeyAuthenticator(_tenantRegistry, Options.Create(new ApiKeyAuthOptions()));
            _tenantRegistry.TryCreate("t_slo", new TenantQuota(), out _, apiKey: TenantApiKey);

            _port = 6000 + new Random().Next(1000);
            var cacheStorage = new MemoryCacheStorage();
            var indexRegistry = VectorCommandSet.SharedIndexRegistry;
            var resultCache = new ResultCache(cacheStorage, indexRegistry);
            var policyEngine = new StaticPolicyEngine(TimeSpan.FromSeconds(60)); // Always cache for tests

            _server = new Garnet.GarnetServer(new string[] { "--port", _port.ToString(), "--bind", "127.0.0.1" });
            _server.Register.NewCommand(
                "VEC.ADD",
                Garnet.server.CommandType.ReadModifyWrite,
                new VectorCommandSet(VectorCommandType.Add, tenantAuthenticator: _tenantAuthenticator),
                new Garnet.server.RespCommandsInfo { Command = (Garnet.server.RespCommand)VectorCommandSet.VEC_ADD, Name = "VEC.ADD" });

            _server.Register.NewCommand(
                "VEC.SEARCH",
                Garnet.server.CommandType.Read,
                new VectorCommandSet(VectorCommandType.Search, resultCache, policyEngine, tenantAuthenticator: _tenantAuthenticator),
                new Garnet.server.RespCommandsInfo { Command = (Garnet.server.RespCommand)VectorCommandSet.VEC_SEARCH, Name = "VEC.SEARCH" });

            _server.Start();
        }

        public void Dispose()
        {
            _server.Dispose();
        }

        [Fact]
        public void CacheHintForce_Miss_ReturnsBusyWithoutComputing()
        {
            using var redis = ConnectionMultiplexer.Connect($"127.0.0.1:{_port}");
            var db = redis.GetDatabase();

            // Ensure the command key exists so Garnet will invoke our Reader implementation.
            db.Execute("SET", "t_slo", "1");

            // Create the index so we don't get "Index Not Found" (P6-2 Fix requires index for metric)
            db.Execute("VEC.ADD", "t_slo", "i_slo", "dummy", "VECTOR", "[1,0]", "API_KEY", TenantApiKey);

            // No data in cache for this specific query, so cache miss is guaranteed.
            AssertServerErrorContains(() =>
                db.Execute("VEC.SEARCH", "t_slo", "i_slo", "TOPK", "1", "VECTOR", "[0,1]", "CACHE_HINT", "force", "API_KEY", TenantApiKey),
                "VEC_ERR_BUSY");
        }

        [Fact]
        public void CacheHintForce_Hit_ReturnsResults()
        {
            using var redis = ConnectionMultiplexer.Connect($"127.0.0.1:{_port}");
            var db = redis.GetDatabase();

            db.Execute("VEC.ADD", "t_slo", "i_slo2", "d1", "VECTOR", "[1,0]", "API_KEY", TenantApiKey);

            // First search populates cache (miss -> compute -> cache set)
            var _ = (RedisResult[]?)db.Execute("VEC.SEARCH", "t_slo", "i_slo2", "TOPK", "1", "VECTOR", "[1,0]", "API_KEY", TenantApiKey);

            // Cache-only mode should return from cache.
            var result = (RedisResult[]?)db.Execute("VEC.SEARCH", "t_slo", "i_slo2", "TOPK", "1", "VECTOR", "[1,0]", "CACHE_HINT", "force", "API_KEY", TenantApiKey);
            Assert.NotNull(result);
            Assert.Single(result!);
            Assert.Equal("d1", ((RedisResult[]?)result![0])![0].ToString());
        }

        private static void AssertServerErrorContains(Func<RedisResult> action, string expected)
        {
            try
            {
                var result = action();
                var message = result.ToString();
                if (string.IsNullOrWhiteSpace(message))
                {
                    message = TryGetErrorString(result);
                }
                Assert.Contains(expected, message);
            }
            catch (RedisServerException ex)
            {
                Assert.Contains(expected, ex.Message);
            }
        }

        private static string TryGetErrorString(RedisResult result)
        {
            if (result.Resp2Type == ResultType.Error)
            {
                try
                {
                    var value = (RedisValue)result;
                    return value.IsNull ? string.Empty : value.ToString();
                }
                catch
                {
                    try
                    {
                        return result.ToString();
                    }
                    catch
                    {
                        return string.Empty;
                    }
                }
            }

            return result.ToString();
        }
    }
}

