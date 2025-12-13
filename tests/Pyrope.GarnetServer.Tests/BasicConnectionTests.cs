using System;
using System.Threading;
using Garnet;
using StackExchange.Redis;
using Xunit;

namespace Pyrope.GarnetServer.Tests
{
    public class BasicConnectionTests : IDisposable
    {
        private readonly Garnet.GarnetServer _server;
        private readonly int _port;

        public BasicConnectionTests()
        {
            _port = 3278 + new Random().Next(1000); // Random port to avoid conflicts
            try
            {
                _server = new Garnet.GarnetServer(new string[] { "--port", _port.ToString(), "--bind", "127.0.0.1" });
                _server.Start();
            }
            catch (Exception)
            {
                // Retry once with different port if failed (simple collision handling)
                _port = 3278 + new Random().Next(1000);
                _server = new Garnet.GarnetServer(new string[] { "--port", _port.ToString(), "--bind", "127.0.0.1" });
                _server.Start();
            }
        }

        public void Dispose()
        {
            _server.Dispose();
        }

        [Fact]
        public void CanConnectAndPing()
        {
            using var redis = ConnectionMultiplexer.Connect($"127.0.0.1:{_port}");
            var db = redis.GetDatabase();
            var pong = db.Execute("PING");
            Assert.Equal("PONG", pong.ToString());
        }
    }
}
