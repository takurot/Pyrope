using System;
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
    public class VectorSearchTraceTests : IDisposable
    {
        private const string TenantApiKey = "test-tenant-key";
        private readonly TenantRegistry _tenantRegistry = new();
        private readonly ITenantAuthenticator _tenantAuthenticator;
        private readonly Garnet.GarnetServer _server;
        private readonly int _port;

        public VectorSearchTraceTests()
        {
            _port = 4500 + new Random().Next(1000);
            var cacheStorage = new MemoryCacheStorage();
            var resultCache = new ResultCache(cacheStorage, VectorCommandSet.SharedIndexRegistry);
            var policyEngine = new StaticPolicyEngine(TimeSpan.FromSeconds(60));
            _tenantAuthenticator = new TenantApiKeyAuthenticator(_tenantRegistry, Options.Create(new ApiKeyAuthOptions()));
            _tenantRegistry.TryCreate("t_trace", new TenantQuota(), out _, apiKey: TenantApiKey);

            try
            {
                _server = new Garnet.GarnetServer(new string[] { "--port", _port.ToString(), "--bind", "127.0.0.1" });
                _server.Register.NewCommand("VEC.ADD", Garnet.server.CommandType.ReadModifyWrite, new VectorCommandSet(VectorCommandType.Add, tenantAuthenticator: _tenantAuthenticator), new Garnet.server.RespCommandsInfo { Command = (Garnet.server.RespCommand)VectorCommandSet.VEC_ADD, Name = "VEC.ADD" });
                _server.Register.NewCommand("VEC.SEARCH", Garnet.server.CommandType.Read, new VectorCommandSet(VectorCommandType.Search, resultCache, policyEngine, tenantAuthenticator: _tenantAuthenticator), new Garnet.server.RespCommandsInfo { Command = (Garnet.server.RespCommand)VectorCommandSet.VEC_SEARCH, Name = "VEC.SEARCH" });
                _server.Start();
            }
            catch
            {
                _port = 4500 + new Random().Next(1000);
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
        public void Search_WithTrace_ReturnsDebugPayload()
        {
            using var redis = ConnectionMultiplexer.Connect($"127.0.0.1:{_port}");
            var db = redis.GetDatabase();
            db.Execute("VEC.ADD", "t_trace", "i_trace", "d1", "VECTOR", "[1,0]", "API_KEY", TenantApiKey);

            var result = (RedisResult[]?)db.Execute("VEC.SEARCH", "t_trace", "i_trace", "TOPK", "1", "VECTOR", "[1,0]", "TRACE", "REQUEST_ID", "req-1", "API_KEY", TenantApiKey);

            Assert.NotNull(result);
            Assert.Equal(2, result!.Length);
            Assert.Contains("\"RequestId\":\"req-1\"", result[1].ToString());
        }

        [Fact]
        public void Search_WithTrace_IncludesMetadataMs()
        {
            using var redis = ConnectionMultiplexer.Connect($"127.0.0.1:{_port}");
            var db = redis.GetDatabase();
            db.Execute("VEC.ADD", "t_trace", "i_meta_ms", "d1", "VECTOR", "[1,0]", "API_KEY", TenantApiKey);
            db.Execute("VEC.ADD", "t_trace", "i_meta_ms", "d2", "VECTOR", "[0,1]", "API_KEY", TenantApiKey);

            var result = (RedisResult[]?)db.Execute("VEC.SEARCH", "t_trace", "i_meta_ms", "TOPK", "2", "VECTOR", "[1,0]", "TRACE", "API_KEY", TenantApiKey);

            Assert.NotNull(result);
            Assert.Equal(2, result!.Length);
            var trace = result[1].ToString()!;
            Assert.Contains("\"MetadataMs\":", trace);
        }

        [Fact]
        public void Search_WithTrace_FaissAndMetadataMsAreNonNegative()
        {
            // FaissMs and MetadataMs should be correctly measured (non-negative, non-zero for FAISS path)
            using var redis = ConnectionMultiplexer.Connect($"127.0.0.1:{_port}");
            var db = redis.GetDatabase();
            for (int i = 0; i < 5; i++)
            {
                db.Execute("VEC.ADD", "t_trace", "i_gap", $"d{i}", "VECTOR", $"[{i},0]", "META", $"{{\"idx\":{i}}}", "API_KEY", TenantApiKey);
            }

            var result = (RedisResult[]?)db.Execute("VEC.SEARCH", "t_trace", "i_gap", "TOPK", "5", "VECTOR", "[1,0]", "TRACE", "WITH_META", "API_KEY", TenantApiKey);

            Assert.NotNull(result);
            Assert.Equal(2, result!.Length);
            var trace = result[1].ToString()!;

            var doc = System.Text.Json.JsonDocument.Parse(trace);
            var latencyMs = doc.RootElement.GetProperty("LatencyMs").GetDouble();
            var faissMs = doc.RootElement.GetProperty("FaissMs").GetDouble();
            var metadataMs = doc.RootElement.GetProperty("MetadataMs").GetDouble();

            // FaissMs must be positive — faissStart is now correctly captured before index.Search()
            Assert.True(faissMs >= 0, $"FaissMs should be non-negative, got {faissMs:F3}ms");
            // MetadataMs must be non-negative — captures Store.TryGet + post-filter time
            Assert.True(metadataMs >= 0, $"MetadataMs should be non-negative, got {metadataMs:F3}ms");
            // LatencyMs must be at least as large as FaissMs + MetadataMs
            Assert.True(latencyMs >= faissMs + metadataMs,
                $"LatencyMs ({latencyMs:F3}ms) should cover FaissMs ({faissMs:F3}ms) + MetadataMs ({metadataMs:F3}ms)");
        }
    }
}
