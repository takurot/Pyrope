using System;
using System.Net.Sockets;
using System.Threading;
using Garnet;
using Garnet.server;
using Microsoft.Extensions.Options;
using Pyrope.GarnetServer.Extensions;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Security;
using Pyrope.GarnetServer.Services;
using StackExchange.Redis;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Extensions
{
    /// <summary>
    /// Integration tests for RESP-level AUTH + VEC commands without per-command API_KEY.
    /// Verifies Issue #52: standard Redis AUTH authenticates the session so subsequent
    /// VEC.* commands work without passing API_KEY on every call.
    /// </summary>
    public sealed class RespAuthIntegrationTests : IDisposable
    {
        private const string Tenant = "t_auth_test";
        private const string ApiKey = "testkey-auth";
        private readonly TenantRegistry _tenantRegistry = new();
        private readonly ITenantAuthenticator _tenantAuthenticator;
        private readonly Garnet.GarnetServer? _server;
        private readonly int _port;

        // Use Interlocked counter to avoid port collisions when tests run in parallel.
        private static int _portCounter = 7300;

        public RespAuthIntegrationTests()
        {
            SessionAuthContext.Reset();

            var opts = Options.Create(new ApiKeyAuthOptions { Enabled = true });
            _tenantAuthenticator = new TenantApiKeyAuthenticator(_tenantRegistry, opts);
            _tenantRegistry.TryCreate(Tenant, new TenantQuota(), out _, apiKey: ApiKey);

            _port = Interlocked.Increment(ref _portCounter);
            var authSettings = new PyropeAuthenticationSettings(_tenantAuthenticator);

            _server = new Garnet.GarnetServer(
                new[] { "--port", _port.ToString(), "--bind", "127.0.0.1" },
                authenticationSettingsOverride: authSettings);

            _server.Register.NewCommand(
                "VEC.ADD",
                CommandType.ReadModifyWrite,
                new VectorCommandSet(VectorCommandType.Add, tenantAuthenticator: _tenantAuthenticator),
                new RespCommandsInfo { Command = (RespCommand)VectorCommandSet.VEC_ADD, Name = "VEC.ADD" });

            _server.Register.NewCommand(
                "VEC.SEARCH",
                CommandType.Read,
                new VectorCommandSet(VectorCommandType.Search, tenantAuthenticator: _tenantAuthenticator),
                new RespCommandsInfo { Command = (RespCommand)VectorCommandSet.VEC_SEARCH, Name = "VEC.SEARCH" });

            _server.Start();
            WaitForServerReady(_port);
        }

        public void Dispose()
        {
            SessionAuthContext.Reset();
            _server?.Dispose();
        }

        /// <summary>Poll until the server accepts TCP connections or the deadline passes.</summary>
        private static void WaitForServerReady(int port, int timeoutMs = 3000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    using var tcp = new TcpClient();
                    tcp.Connect("127.0.0.1", port);
                    return;
                }
                catch
                {
                    Thread.Sleep(50);
                }
            }
            throw new TimeoutException($"Server did not start on port {port} within {timeoutMs}ms.");
        }

        [Fact]
        public void VecAdd_WithSessionAuth_NoApiKey_Succeeds()
        {
            // Connect with password = "tenantId:apiKey" (single-arg AUTH)
            var cfg = ConfigurationOptions.Parse($"127.0.0.1:{_port}");
            cfg.Password = $"{Tenant}:{ApiKey}";
            using var redis = ConnectionMultiplexer.Connect(cfg);
            var db = redis.GetDatabase();

            // VEC.ADD without API_KEY — should succeed because AUTH set the session
            var result = db.Execute("VEC.ADD", Tenant, "idx1", "doc1",
                "VECTOR", "[1.0,2.0,3.0]");

            Assert.Equal("VEC_OK", result.ToString());
        }

        [Fact]
        public void VecAdd_NoAuth_NoApiKey_Fails()
        {
            // No AUTH, no API_KEY → should fail
            using var redis = ConnectionMultiplexer.Connect($"127.0.0.1:{_port},allowAdmin=true");
            var db = redis.GetDatabase();

            var ex = Assert.Throws<RedisServerException>(() =>
                db.Execute("VEC.ADD", Tenant, "idx3", "doc3",
                    "VECTOR", "[1.0,0.0,0.0]"));

            Assert.Contains("VEC_ERR_AUTH", ex.Message);
        }

        [Fact]
        public void VecAdd_WithApiKey_StillWorks()
        {
            // Per-command API_KEY should still work (backward compatibility)
            using var redis = ConnectionMultiplexer.Connect($"127.0.0.1:{_port}");
            var db = redis.GetDatabase();

            var result = db.Execute("VEC.ADD", Tenant, "idx4", "doc4",
                "VECTOR", "[7.0,8.0,9.0]",
                "API_KEY", ApiKey);

            Assert.Equal("VEC_OK", result.ToString());
        }
    }
}
