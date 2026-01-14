using System;
using System.Text.Json;
using System.Threading;
using Garnet;
using Garnet.server;
using Pyrope.GarnetServer.Extensions;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Security;
using Pyrope.GarnetServer.Services;
using StackExchange.Redis;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Extensions
{
    public class CostAwareQueryTests : IDisposable
    {
        private const string TenantApiKey = "cost-test-key";
        private readonly TenantRegistry _tenantRegistry = new();
        private readonly ITenantAuthenticator _tenantAuthenticator;
        private readonly ITenantQuotaEnforcer _quotaEnforcer;
        private readonly Garnet.GarnetServer _server;
        private readonly int _port;

        public CostAwareQueryTests()
        {
            _port = 4500 + new Random().Next(1000);
            _tenantAuthenticator = new TenantApiKeyAuthenticator(_tenantRegistry);
            _quotaEnforcer = new TenantQuotaEnforcer(_tenantRegistry, new Pyrope.GarnetServer.Services.SystemTimeProvider());

            // Setup tenant with LOW budget
            EnsureTenant("tenant_poor", budget: 1E-9); // Extremely low budget, should trigger immediately
            EnsureTenant("tenant_rich", budget: 1000.0);

            try
            {
                _server = new Garnet.GarnetServer(new string[] { "--port", _port.ToString(), "--bind", "127.0.0.1" });
                RegisterCommands();
                _server.Start();
            }
            catch
            {
                _port = 4500 + new Random().Next(1000);
                _server = new Garnet.GarnetServer(new string[] { "--port", _port.ToString(), "--bind", "127.0.0.1" });
                RegisterCommands();
                _server.Start();
            }
        }

        private void RegisterCommands()
        {
             RegisterCommand("VEC.ADD", VectorCommandType.Add);
             RegisterCommand("VEC.DEL", VectorCommandType.Del);
             RegisterCommand("VEC.SEARCH", VectorCommandType.Search);
        }

        private void RegisterCommand(string name, VectorCommandType type)
        {
             var cmdSet = new VectorCommandSet(
                type,
                resultCache: null,
                policyEngine: null,
                metrics: null,
                lshService: null,
                quotaEnforcer: _quotaEnforcer,
                tenantAuthenticator: _tenantAuthenticator
            );

            _server.Register.NewCommand(name, 
                type == VectorCommandType.Search || type == VectorCommandType.Stats ? CommandType.Read : CommandType.ReadModifyWrite,
                cmdSet,
                new RespCommandsInfo { Command = (RespCommand)(int)(type switch {
                    VectorCommandType.Add => VectorCommandSet.VEC_ADD,
                    VectorCommandType.Upsert => VectorCommandSet.VEC_UPSERT,
                    VectorCommandType.Del => VectorCommandSet.VEC_DEL,
                    VectorCommandType.Search => VectorCommandSet.VEC_SEARCH,
                    VectorCommandType.Stats => VectorCommandSet.VEC_STATS,
                    _ => 0
                }), Name = name }
            );
        }

        public void Dispose()
        {
            _server.Dispose();
        }

        [Fact]
        public void Search_WhenOverBudget_DegradesAndReturnsAdjustmentInfo()
        {
            // 1. Init
            using var redis = ConnectionMultiplexer.Connect($"127.0.0.1:{_port}");
            var db = redis.GetDatabase();

            RegisterCommandsReal(); // Call helper to register

            // 2. Add data
            var add = db.Execute("VEC.ADD", "tenant_poor", "idx1", "doc1", "VECTOR", "[1,0]", "API_KEY", TenantApiKey + "-tenant_poor");
            Assert.Equal("VEC_OK", add.ToString());

            // 3. Search ONCE to consume budget (Budget is 0.0001, cost is roughly Count/10000 * Dim/128 ?? No logic is CostCalculator)
            // Logic: Count/10000 * Dim/128.
            // 1 vector, 2 dim. Cost = 1/10000 * 2/128 = 0.0001 * 0.015 = very small.
            // We need to trigger "Over Budget".
            // Since we use accumulated cost, we can search multiple times.
            // First search will record cost.
            
            for(int i=0; i<10; i++)
            {
                 db.Execute("VEC.SEARCH", "tenant_poor", "idx1", "TOPK", "1", "VECTOR", "[1,0]", "API_KEY", TenantApiKey + "-tenant_poor");
            }

            // 4. Verify Over Budget behavior with TRACE
            var result = db.Execute("VEC.SEARCH", "tenant_poor", "idx1", "TOPK", "1", "VECTOR", "[1,0]", "TRACE", "API_KEY", TenantApiKey + "-tenant_poor");
            
            // Result format: [ [hits], traceJson ]
            var arr = (RedisResult[])result;
            Assert.Equal(2, arr.Length);
            
            var traceJson = arr[1].ToString();
            Assert.Contains("budget_exceeded", traceJson);
            Assert.Contains("BudgetAdjustment", traceJson);
            Assert.Contains("original_max_scans", traceJson);
        }

        [Fact]
        public void Search_WhenUnderBudget_NoDegradation()
        {
             using var redis = ConnectionMultiplexer.Connect($"127.0.0.1:{_port}");
            var db = redis.GetDatabase();
            RegisterCommandsReal();

            var add = db.Execute("VEC.ADD", "tenant_rich", "idx1", "doc1", "VECTOR", "[1,0]", "API_KEY", TenantApiKey + "-tenant_rich");
            Assert.Equal("VEC_OK", add.ToString());

            var result = db.Execute("VEC.SEARCH", "tenant_rich", "idx1", "TOPK", "1", "VECTOR", "[1,0]", "TRACE", "API_KEY", TenantApiKey + "-tenant_rich");

            var arr = (RedisResult[])result;
            var traceJson = arr[1].ToString();
            
            // Should NOT contain adjustment or reason
            Assert.DoesNotContain("budget_exceeded", traceJson);
        }

        private void RegisterCommandsReal()
        {
             RegisterCommand("VEC.ADD", VectorCommandType.Add);
             RegisterCommand("VEC.DEL", VectorCommandType.Del);
             RegisterCommand("VEC.SEARCH", VectorCommandType.Search);
        }

        private void EnsureTenant(string tenantId, double budget)
        {
            var apiKey = $"{TenantApiKey}-{tenantId}";
            var quota = new TenantQuota { MonthlyBudget = budget, MaxQps = 1000 };
            _tenantRegistry.TryCreate(tenantId, quota, out _, apiKey: apiKey);
        }
    }
}
