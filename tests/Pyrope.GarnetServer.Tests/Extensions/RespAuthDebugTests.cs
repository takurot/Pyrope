using System;
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
    /// <summary>Debug test to isolate what's causing RespAuthIntegrationTests to fail.</summary>
    public sealed class RespAuthDebugTests : IDisposable
    {
        private const string Tenant = "t_debug";
        private const string ApiKey = "debugkey";
        private readonly TenantRegistry _tenantRegistry = new();
        private readonly Garnet.GarnetServer _server;
        private readonly int _port;
        private readonly Exception? _startupException;

        public RespAuthDebugTests()
        {
            SessionAuthContext.Reset();
            var opts = Options.Create(new ApiKeyAuthOptions { Enabled = true });
            var tenantAuth = new TenantApiKeyAuthenticator(_tenantRegistry, opts);
            _tenantRegistry.TryCreate(Tenant, new TenantQuota(), out _, apiKey: ApiKey);

            _port = 7700 + new Random().Next(200);

            try
            {
                var authSettings = new PyropeAuthenticationSettings(tenantAuth);
                _server = new Garnet.GarnetServer(
                    new[] { "--port", _port.ToString(), "--bind", "127.0.0.1" },
                    authenticationSettingsOverride: authSettings);

                _server.Register.NewCommand(
                    "VEC.ADD", CommandType.ReadModifyWrite,
                    new VectorCommandSet(VectorCommandType.Add, tenantAuthenticator: tenantAuth),
                    new RespCommandsInfo { Command = (RespCommand)VectorCommandSet.VEC_ADD, Name = "VEC.ADD" });

                _server.Start();
                System.Threading.Thread.Sleep(300);
            }
            catch (Exception ex)
            {
                _startupException = ex;
                _server = null!;
            }
        }

        public void Dispose()
        {
            SessionAuthContext.Reset();
            _server?.Dispose();
        }

        [Fact]
        public void ServerStarted_NoException()
        {
            Assert.Null(_startupException);
            Assert.NotNull(_server);
        }

        [Fact]
        public void CanConnect_WithoutPassword()
        {
            if (_startupException != null)
                throw new Exception($"Server failed to start: {_startupException.Message}", _startupException);

            using var redis = ConnectionMultiplexer.Connect($"127.0.0.1:{_port},connectTimeout=2000,abortConnect=false");
            Assert.True(redis.IsConnected, "Should be able to connect without password");
        }

        [Fact]
        public void CanConnect_WithPassword()
        {
            if (_startupException != null)
                throw new Exception($"Server failed to start: {_startupException.Message}", _startupException);

            var cfg = ConfigurationOptions.Parse($"127.0.0.1:{_port}");
            cfg.Password = $"{Tenant}:{ApiKey}";
            cfg.ConnectTimeout = 2000;
            cfg.AbortOnConnectFail = false;
            using var redis = ConnectionMultiplexer.Connect(cfg);
            Assert.True(redis.IsConnected, "Should connect with valid password");
        }
    }
}
