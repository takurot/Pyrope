using System;
using System.Collections.Generic;
using System.Threading;
using Garnet;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Services;
using StackExchange.Redis;
using Xunit;

namespace Pyrope.GarnetServer.Tests
{
    public class IndexMetadataTests : IDisposable
    {
        private readonly Garnet.GarnetServer _server;
        private readonly int _port;
        private readonly IndexMetadataManager _manager;

        public IndexMetadataTests()
        {
            _port = 4000 + new Random().Next(1000);
            try
            {
                _server = new Garnet.GarnetServer(new string[] { "--port", _port.ToString(), "--bind", "127.0.0.1" });
                _server.Start();
            }
            catch
            {
                _port = 4000 + new Random().Next(1000);
                _server = new Garnet.GarnetServer(new string[] { "--port", _port.ToString(), "--bind", "127.0.0.1" });
                _server.Start();
            }
            _manager = new IndexMetadataManager();
        }

        public void Dispose()
        {
            _server.Dispose();
        }

        [Fact]
        public void CanSerializeAndDeserializeConfig()
        {
            var config = new IndexConfig
            {
                Dimension = 128,
                Metric = "COSINE",
                Algorithm = "HNSW",
                Parameters = new Dictionary<string, object> { { "M", 16 }, { "efConstruction", 200 } }
            };

            var bytes = _manager.SerializeConfig(config);
            var deserialized = _manager.DeserializeConfig(bytes);

            Assert.NotNull(deserialized);
            Assert.Equal(config.Dimension, deserialized.Dimension);
            Assert.Equal(config.Metric, deserialized.Metric);
            Assert.Equal(config.Algorithm, deserialized.Algorithm);
            Assert.Equal(config.Parameters["M"].ToString(), deserialized.Parameters["M"].ToString());
        }

        [Fact]
        public void CanStoreAndRetrieveConfigInGarnet()
        {
            using var redis = ConnectionMultiplexer.Connect($"127.0.0.1:{_port}");
            var db = redis.GetDatabase();

            var tenant = "tenantA";
            var index = "idx1";
            var key = _manager.GetMetadataKey(tenant, index);

            var config = new IndexConfig
            {
                Dimension = 768,
                Metric = "IP"
            };

            var jsonBytes = _manager.SerializeConfig(config);
            
            // Store in Garnet
            db.StringSet(key, jsonBytes);

            // Retrieve
            var retrievedValue = db.StringGet(key);
            Assert.True(retrievedValue.HasValue);

            var loadedConfig = _manager.DeserializeConfig((byte[])retrievedValue);
            
            Assert.NotNull(loadedConfig);
            Assert.Equal(768, loadedConfig.Dimension);
            Assert.Equal("IP", loadedConfig.Metric);
        }
        
        [Fact]
        public void GetMetadataKey_ValidatesInputs()
        {
            Assert.Throws<ArgumentException>(() => _manager.GetMetadataKey("", "idx"));
            Assert.Throws<ArgumentException>(() => _manager.GetMetadataKey("tenant", ""));
        }
    }
}
