using System;
using Garnet;
using Microsoft.Extensions.Options;
using Pyrope.GarnetServer.Extensions;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Security;
using Pyrope.GarnetServer.Services;
using StackExchange.Redis;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Extensions
{
    public sealed class NoisyNeighborMitigationTests : IDisposable
    {
        private const string ApiKey = "test-tenant-key";
        private readonly TenantRegistry _registry = new();
        private readonly ITenantAuthenticator _auth;
        private readonly ISloGuardrails _guardrails;
        private readonly Garnet.GarnetServer _server;
        private readonly int _port;

        public NoisyNeighborMitigationTests()
        {
            var highKey = ApiKey + "-high";
            var lowKey = ApiKey + "-low";
            _registry.TryCreate("t_high", new TenantQuota { Priority = 0 }, out _, apiKey: highKey);
            _registry.TryCreate("t_low", new TenantQuota { Priority = 2 }, out _, apiKey: lowKey);
            _auth = new TenantApiKeyAuthenticator(_registry, Options.Create(new ApiKeyAuthOptions()));

            var options = Options.Create(new SloGuardrailsOptions
            {
                Enabled = true,
                TargetP99Ms = 10,
                RecoveryFactor = 0.8,
                DegradedMaxScans = 1000
            });
            _guardrails = new SloGuardrails(options, _registry);
            _guardrails.UpdateLatencyP99(999); // force degraded

            _port = 7000 + new Random().Next(1000);
            _server = new Garnet.GarnetServer(new string[] { "--port", _port.ToString(), "--bind", "127.0.0.1" });
            _server.Register.NewCommand(
                "VEC.ADD",
                Garnet.server.CommandType.ReadModifyWrite,
                new VectorCommandSet(VectorCommandType.Add, tenantAuthenticator: _auth),
                new Garnet.server.RespCommandsInfo { Command = (Garnet.server.RespCommand)VectorCommandSet.VEC_ADD, Name = "VEC.ADD" });

            _server.Register.NewCommand(
                "VEC.SEARCH",
                Garnet.server.CommandType.Read,
                new VectorCommandSet(VectorCommandType.Search, null, null, null, null, null, _auth, _guardrails),
                new Garnet.server.RespCommandsInfo { Command = (Garnet.server.RespCommand)VectorCommandSet.VEC_SEARCH, Name = "VEC.SEARCH" });

            _server.Start();
        }

        public void Dispose()
        {
            _server.Dispose();
        }

        [Fact]
        public void LowPriorityTenant_IsShed_WhenDegraded_AndCacheMiss()
        {
            var highKey = ApiKey + "-high";
            var lowKey = ApiKey + "-low";
            using var redis = ConnectionMultiplexer.Connect($"127.0.0.1:{_port}");
            var db = redis.GetDatabase();

            db.Execute("VEC.ADD", "t_high", "i_nn", "d1", "VECTOR", "[1,0]", "API_KEY", highKey);
            db.Execute("VEC.ADD", "t_low", "i_nn", "d1", "VECTOR", "[1,0]", "API_KEY", lowKey);

            // High priority proceeds normally.
            var ok = (RedisResult[]?)db.Execute("VEC.SEARCH", "t_high", "i_nn", "TOPK", "1", "VECTOR", "[1,0]", "API_KEY", highKey);
            Assert.NotNull(ok);
            Assert.Single(ok!);

            // Low priority is shed (cache-only mode), since caching isn't configured => miss.
            AssertServerErrorContains(() =>
                db.Execute("VEC.SEARCH", "t_low", "i_nn", "TOPK", "1", "VECTOR", "[1,0]", "API_KEY", lowKey),
                "VEC_ERR_BUSY");
        }

        private static void AssertServerErrorContains(Func<RedisResult> action, string expected)
        {
            try
            {
                var result = action();
                Assert.Contains(expected, result.ToString());
            }
            catch (RedisServerException ex)
            {
                Assert.Contains(expected, ex.Message);
            }
        }
    }
}

