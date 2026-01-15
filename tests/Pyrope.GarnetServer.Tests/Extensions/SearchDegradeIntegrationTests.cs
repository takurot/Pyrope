using System;
using Garnet;
using Pyrope.GarnetServer.Extensions;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Security;
using Microsoft.Extensions.Options;
using Pyrope.GarnetServer.Services;
using Pyrope.GarnetServer.Vector;
using StackExchange.Redis;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Extensions
{
    public sealed class SearchDegradeIntegrationTests : IDisposable
    {
        private const string TenantApiKey = "test-tenant-key";
        private readonly TenantRegistry _tenantRegistry = new();
        private readonly ITenantAuthenticator _tenantAuthenticator;
        private readonly ISloGuardrails _guardrails;
        private readonly Garnet.GarnetServer _server;
        private readonly int _port;

        public SearchDegradeIntegrationTests()
        {
            _tenantAuthenticator = new TenantApiKeyAuthenticator(_tenantRegistry, Options.Create(new ApiKeyAuthOptions()));
            _tenantRegistry.TryCreate("t_degrade", new TenantQuota(), out _, apiKey: TenantApiKey);

            _guardrails = new AlwaysDegradeGuardrails();

            _port = 6500 + new Random().Next(1000);
            _server = new Garnet.GarnetServer(new string[] { "--port", _port.ToString(), "--bind", "127.0.0.1" });
            _server.Register.NewCommand(
                "VEC.ADD",
                Garnet.server.CommandType.ReadModifyWrite,
                new VectorCommandSet(VectorCommandType.Add, tenantAuthenticator: _tenantAuthenticator),
                new Garnet.server.RespCommandsInfo { Command = (Garnet.server.RespCommand)VectorCommandSet.VEC_ADD, Name = "VEC.ADD" });

            _server.Register.NewCommand(
                "VEC.SEARCH",
                Garnet.server.CommandType.Read,
                new VectorCommandSet(VectorCommandType.Search, null, null, null, null, null, _tenantAuthenticator, _guardrails),
                new Garnet.server.RespCommandsInfo { Command = (Garnet.server.RespCommand)VectorCommandSet.VEC_SEARCH, Name = "VEC.SEARCH" });

            _server.Start();
        }

        public void Dispose()
        {
            _server.Dispose();
        }

        [Fact]
        public void Search_WhenDegraded_UsesSearchOptions()
        {
            using var redis = ConnectionMultiplexer.Connect($"127.0.0.1:{_port}");
            var db = redis.GetDatabase();

            db.Execute("VEC.ADD", "t_degrade", "i_degrade", "d1", "VECTOR", "[1,0]", "API_KEY", TenantApiKey);

            // Guardrails forces MaxScans=0, so search returns empty even though data exists.
            var results = (RedisResult[]?)db.Execute("VEC.SEARCH", "t_degrade", "i_degrade", "TOPK", "1", "VECTOR", "[1,0]", "API_KEY", TenantApiKey);
            Assert.NotNull(results);
            Assert.Empty(results!);
        }

        private sealed class AlwaysDegradeGuardrails : ISloGuardrails
        {
            public bool IsDegraded => true;
            public double LastP99Ms => 999;
            public SearchOptions? GetSearchOptions(string tenantId, string indexName) => new SearchOptions(MaxScans: 0);
            public bool ShouldForceCacheOnly(string tenantId, string indexName) => false;
            public void UpdateLatencyP99(double p99Ms) { }
        }
    }
}

