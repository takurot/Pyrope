using System;
using Garnet;
using Pyrope.GarnetServer.Extensions;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Policies;
using StackExchange.Redis;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Extensions
{
    public class VectorSearchTraceTests : IDisposable
    {
        private readonly Garnet.GarnetServer _server;
        private readonly int _port;

        public VectorSearchTraceTests()
        {
            _port = 4500 + new Random().Next(1000);
            var cacheStorage = new MemoryCacheStorage();
            var resultCache = new ResultCache(cacheStorage, VectorCommandSet.SharedIndexRegistry);
            var policyEngine = new StaticPolicyEngine(TimeSpan.FromSeconds(60));

            try
            {
                _server = new Garnet.GarnetServer(new string[] { "--port", _port.ToString(), "--bind", "127.0.0.1" });
                _server.Register.NewCommand("VEC.ADD", Garnet.server.CommandType.ReadModifyWrite, new VectorCommandSet(VectorCommandType.Add), new Garnet.server.RespCommandsInfo { Command = (Garnet.server.RespCommand)VectorCommandSet.VEC_ADD, Name = "VEC.ADD" });
                _server.Register.NewCommand("VEC.SEARCH", Garnet.server.CommandType.Read, new VectorCommandSet(VectorCommandType.Search, resultCache, policyEngine), new Garnet.server.RespCommandsInfo { Command = (Garnet.server.RespCommand)VectorCommandSet.VEC_SEARCH, Name = "VEC.SEARCH" });
                _server.Start();
            }
            catch
            {
                _port = 4500 + new Random().Next(1000);
                _server = new Garnet.GarnetServer(new string[] { "--port", _port.ToString(), "--bind", "127.0.0.1" });
                _server.Register.NewCommand("VEC.ADD", Garnet.server.CommandType.ReadModifyWrite, new VectorCommandSet(VectorCommandType.Add), new Garnet.server.RespCommandsInfo { Command = (Garnet.server.RespCommand)VectorCommandSet.VEC_ADD, Name = "VEC.ADD" });
                _server.Register.NewCommand("VEC.SEARCH", Garnet.server.CommandType.Read, new VectorCommandSet(VectorCommandType.Search, resultCache, policyEngine), new Garnet.server.RespCommandsInfo { Command = (Garnet.server.RespCommand)VectorCommandSet.VEC_SEARCH, Name = "VEC.SEARCH" });
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
            db.Execute("VEC.ADD", "t_trace", "i_trace", "d1", "VECTOR", "[1,0]");

            var result = (RedisResult[]?)db.Execute("VEC.SEARCH", "t_trace", "i_trace", "TOPK", "1", "VECTOR", "[1,0]", "TRACE", "REQUEST_ID", "req-1");

            Assert.NotNull(result);
            Assert.Equal(2, result!.Length);
            Assert.Contains("\"RequestId\":\"req-1\"", result[1].ToString());
        }
    }
}
