using System;
using System.Threading;
using Garnet;
using Garnet.server;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Security;
using Pyrope.GarnetServer.Services;
using StackExchange.Redis;
using Xunit;

namespace Pyrope.GarnetServer.Tests
{
    public class CommandRegistryTests : IDisposable
    {
        private const string TenantApiKey = "test-tenant-key";
        private readonly TenantRegistry _tenantRegistry = new();
        private readonly ITenantAuthenticator _tenantAuthenticator;
        private readonly Garnet.GarnetServer _server;
        private readonly int _port;

        public CommandRegistryTests()
        {
            _port = 3278 + new Random().Next(1000);
            _tenantAuthenticator = new TenantApiKeyAuthenticator(_tenantRegistry);

            EnsureTenant("tenant1");
            EnsureTenant("tenant_search1");
            EnsureTenant("tenant3");
            EnsureTenant("tenant2");
            EnsureTenant("tenant_dim");
            EnsureTenant("tenant_missing");
            try
            {
                // Determine port availability
                _server = new Garnet.GarnetServer(new string[] { "--port", _port.ToString(), "--bind", "127.0.0.1" });

                _server.Register.NewCommand("VEC.ADD", Garnet.server.CommandType.ReadModifyWrite, new Pyrope.GarnetServer.Extensions.VectorCommandSet(Pyrope.GarnetServer.Extensions.VectorCommandType.Add, tenantAuthenticator: _tenantAuthenticator), new Garnet.server.RespCommandsInfo { Command = (Garnet.server.RespCommand)Pyrope.GarnetServer.Extensions.VectorCommandSet.VEC_ADD, Name = "VEC.ADD" });
                _server.Register.NewCommand("VEC.DEL", Garnet.server.CommandType.ReadModifyWrite, new Pyrope.GarnetServer.Extensions.VectorCommandSet(Pyrope.GarnetServer.Extensions.VectorCommandType.Del, tenantAuthenticator: _tenantAuthenticator), new Garnet.server.RespCommandsInfo { Command = (Garnet.server.RespCommand)Pyrope.GarnetServer.Extensions.VectorCommandSet.VEC_DEL, Name = "VEC.DEL" });
                _server.Register.NewCommand("VEC.SEARCH", Garnet.server.CommandType.Read, new Pyrope.GarnetServer.Extensions.VectorCommandSet(Pyrope.GarnetServer.Extensions.VectorCommandType.Search, tenantAuthenticator: _tenantAuthenticator), new Garnet.server.RespCommandsInfo { Command = (Garnet.server.RespCommand)Pyrope.GarnetServer.Extensions.VectorCommandSet.VEC_SEARCH, Name = "VEC.SEARCH" });

                _server.Start();
            }
            catch
            {
                _port = 3278 + new Random().Next(1000);
                _server = new Garnet.GarnetServer(new string[] { "--port", _port.ToString(), "--bind", "127.0.0.1" });

                _server.Register.NewCommand("VEC.ADD", Garnet.server.CommandType.ReadModifyWrite, new Pyrope.GarnetServer.Extensions.VectorCommandSet(Pyrope.GarnetServer.Extensions.VectorCommandType.Add, tenantAuthenticator: _tenantAuthenticator), new Garnet.server.RespCommandsInfo { Command = (Garnet.server.RespCommand)Pyrope.GarnetServer.Extensions.VectorCommandSet.VEC_ADD, Name = "VEC.ADD" });
                _server.Register.NewCommand("VEC.DEL", Garnet.server.CommandType.ReadModifyWrite, new Pyrope.GarnetServer.Extensions.VectorCommandSet(Pyrope.GarnetServer.Extensions.VectorCommandType.Del, tenantAuthenticator: _tenantAuthenticator), new Garnet.server.RespCommandsInfo { Command = (Garnet.server.RespCommand)Pyrope.GarnetServer.Extensions.VectorCommandSet.VEC_DEL, Name = "VEC.DEL" });
                _server.Register.NewCommand("VEC.SEARCH", Garnet.server.CommandType.Read, new Pyrope.GarnetServer.Extensions.VectorCommandSet(Pyrope.GarnetServer.Extensions.VectorCommandType.Search, tenantAuthenticator: _tenantAuthenticator), new Garnet.server.RespCommandsInfo { Command = (Garnet.server.RespCommand)Pyrope.GarnetServer.Extensions.VectorCommandSet.VEC_SEARCH, Name = "VEC.SEARCH" });

                _server.Start();
            }
        }

        public void Dispose()
        {
            _server.Dispose();
        }

        [Fact]
        public void VecAdd_ReturnsStubResponse()
        {
            using var redis = ConnectionMultiplexer.Connect($"127.0.0.1:{_port}");
            var db = redis.GetDatabase();

            // VEC.ADD tenant index id VECTOR <json> META <json>
            // For now, we just check if it accepts the command and returns a stub/OK
            // Since the components aren't registered yet, this should fail with "unknown command"
            var result = db.Execute("VEC.ADD", "tenant1", "idx1", "doc1", "VECTOR", "[1,2]", "META", "{\"category\":\"news\"}", "API_KEY", TenantApiKey + "-tenant1");

            Assert.Equal("VEC_OK", result.ToString());
        }

        [Fact]
        public void VecSearch_ReturnsTopKResults()
        {
            using var redis = ConnectionMultiplexer.Connect($"127.0.0.1:{_port}");
            var db = redis.GetDatabase();

            var addOne = db.Execute("VEC.ADD", "tenant_search1", "idx_search1", "doc1", "VECTOR", "[1,0]", "META", "{\"category\":\"news\"}", "API_KEY", TenantApiKey + "-tenant_search1");
            Assert.Equal("VEC_OK", addOne.ToString());
            var addTwo = db.Execute("VEC.ADD", "tenant_search1", "idx_search1", "doc2", "VECTOR", "[0,1]", "TAGS", "sports", "API_KEY", TenantApiKey + "-tenant_search1");
            Assert.Equal("VEC_OK", addTwo.ToString());

            var rawResult = db.Execute("VEC.SEARCH", "tenant_search1", "idx_search1", "TOPK", "1", "VECTOR", "[1,0]", "API_KEY", TenantApiKey + "-tenant_search1");
            var result = (RedisResult[]?)rawResult;
            Assert.NotNull(result);
            Assert.Single(result!);

            var hit = (RedisResult[]?)result![0];
            Assert.NotNull(hit);
            Assert.Equal("doc1", hit[0].ToString());
        }

        [Fact]
        public void VecSearch_AppliesTagFilterAndReturnsMeta()
        {
            using var redis = ConnectionMultiplexer.Connect($"127.0.0.1:{_port}");
            var db = redis.GetDatabase();

            var addOne = db.Execute("VEC.ADD", "tenant3", "idx3", "doc1", "VECTOR", "[1,0]", "TAGS", "[\"news\",\"sports\"]", "META", "{\"category\":\"news\"}", "API_KEY", TenantApiKey + "-tenant3");
            Assert.Equal("VEC_OK", addOne.ToString());
            var addTwo = db.Execute("VEC.ADD", "tenant3", "idx3", "doc2", "VECTOR", "[0,1]", "TAGS", "sports", "API_KEY", TenantApiKey + "-tenant3");
            Assert.Equal("VEC_OK", addTwo.ToString());

            var rawResult = db.Execute("VEC.SEARCH", "tenant3", "idx3", "TOPK", "5", "VECTOR", "[1,0]", "FILTER", "news", "WITH_META", "API_KEY", TenantApiKey + "-tenant3");
            var result = (RedisResult[]?)rawResult;
            Assert.NotNull(result);
            Assert.Single(result!);

            var hit = (RedisResult[]?)result![0];
            Assert.NotNull(hit);
            Assert.Equal(3, hit!.Length);
            Assert.Equal("doc1", hit[0].ToString());
            Assert.Equal("{\"category\":\"news\"}", hit[2].ToString());
        }

        [Fact]
        public void VecDel_ReturnsOk()
        {
            using var redis = ConnectionMultiplexer.Connect($"127.0.0.1:{_port}");
            var db = redis.GetDatabase();

            var addResult = db.Execute("VEC.ADD", "tenant2", "idx2", "doc2", "VECTOR", "[1,2]", "API_KEY", TenantApiKey + "-tenant2");
            Assert.Equal("VEC_OK", addResult.ToString());

            var delResult = db.Execute("VEC.DEL", "tenant2", "idx2", "doc2", "API_KEY", TenantApiKey + "-tenant2");

            Assert.Equal("VEC_OK", delResult.ToString());

            var secondDelete = db.Execute("VEC.DEL", "tenant2", "idx2", "doc2", "API_KEY", TenantApiKey + "-tenant2");
            Assert.Equal("VEC_OK", secondDelete.ToString());
        }

        [Fact]
        public void VecAdd_ReturnsDimMismatchError()
        {
            using var redis = ConnectionMultiplexer.Connect($"127.0.0.1:{_port}");
            var db = redis.GetDatabase();

            var addResult = db.Execute("VEC.ADD", "tenant_dim", "idx_dim", "doc1", "VECTOR", "[1,2]", "API_KEY", TenantApiKey + "-tenant_dim");
            Assert.Equal("VEC_OK", addResult.ToString());

            AssertServerErrorContains(() =>
                db.Execute("VEC.ADD", "tenant_dim", "idx_dim", "doc2", "VECTOR", "[1,2,3]", "API_KEY", TenantApiKey + "-tenant_dim"),
                "VEC_ERR_DIM");
        }

        [Fact]
        public void VecSearch_ReturnsNotFoundErrorWhenIndexMissing()
        {
            using var redis = ConnectionMultiplexer.Connect($"127.0.0.1:{_port}");
            var db = redis.GetDatabase();

            var seed = db.Execute("VEC.ADD", "tenant_missing", "idx_existing", "doc1", "VECTOR", "[1,2]", "API_KEY", TenantApiKey + "-tenant_missing");
            Assert.Equal("VEC_OK", seed.ToString());

            AssertServerErrorContains(() =>
                db.Execute("VEC.SEARCH", "tenant_missing", "idx_missing", "TOPK", "1", "VECTOR", "[1,2]", "API_KEY", TenantApiKey + "-tenant_missing"),
                "VEC_ERR_NOT_FOUND");
        }

        [Fact]
        public void VecAdd_MissingApiKey_ReturnsAuthError()
        {
            using var redis = ConnectionMultiplexer.Connect($"127.0.0.1:{_port}");
            var db = redis.GetDatabase();

            AssertServerErrorContains(() =>
                db.Execute("VEC.ADD", "tenant1", "idx1", "doc_missing_key", "VECTOR", "[1,2]"),
                "VEC_ERR_AUTH");
        }

        private void EnsureTenant(string tenantId)
        {
            // Use unique API keys for each tenant in tests to satisfy the new global uniqueness constraint
            var apiKey = $"{TenantApiKey}-{tenantId}";
            _tenantRegistry.TryCreate(tenantId, new TenantQuota(), out _, apiKey: apiKey);
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
                        return result.ToString();
                    }
                }
            }

            return result.ToString();
        }
    }
}
