using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pyrope.GarnetServer;
using Pyrope.GarnetServer.Controllers;
using Pyrope.GarnetServer.Services;
using Pyrope.GarnetServer.Vector;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Api
{
    public class IndexApiTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private const string AdminApiKey = "test-admin-key";
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;

        public IndexApiTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new[]
                    {
                        new KeyValuePair<string, string?>("Auth:Enabled", "true"),
                        new KeyValuePair<string, string?>("Auth:AdminApiKey", AdminApiKey)
                    });
                });

                builder.ConfigureServices(services =>
                {
                    // Safely remove GarnetService to avoid starting Garnet during tests
                    services.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();
                });
            });
            _client = _factory.CreateClient();
            _client.DefaultRequestHeaders.Add("X-API-KEY", AdminApiKey);
        }

        [Fact]
        public async Task CreateIndex_ReturnsCreated()
        {
            var request = new CreateIndexRequest
            {
                TenantId = "tenant1",
                IndexName = "index1",
                Dimension = 128,
                Metric = "L2"
            };

            var response = await _client.PostAsync("/v1/indexes",
                new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"));

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                throw new Exception($"API Failed: {response.StatusCode} {err}");
            }
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        [Fact]
        public async Task CreateIndex_InvalidRequest_ReturnsBadRequest()
        {
            var request = new CreateIndexRequest
            {
                TenantId = "", // Invalid
                IndexName = "index1",
                Dimension = 128
            };

            var response = await _client.PostAsync("/v1/indexes",
                new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task GetStats_ReturnsStats()
        {
            var tenantId = "tenant2";
            var indexName = "index2";

            // Create first
            var createRequest = new CreateIndexRequest
            {
                TenantId = tenantId,
                IndexName = indexName,
                Dimension = 64,
                Metric = "Cosine"
            };
            await _client.PostAsync("/v1/indexes",
                new StringContent(JsonSerializer.Serialize(createRequest), Encoding.UTF8, "application/json"));

            // Get Stats
            var response = await _client.GetAsync($"/v1/indexes/{tenantId}/{indexName}/stats");

            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            var stats = JsonSerializer.Deserialize<IndexStats>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            Assert.NotNull(stats);
            Assert.Equal(64, stats.Dimension);
            Assert.Equal("Cosine", stats.Metric);
            Assert.Equal(0, stats.Count);
        }

        [Fact]
        public async Task SnapshotAndLoad_Success()
        {
            var tenantId = "tenant3";
            var indexName = "index3";
            var snapshotPath = Path.Combine(Path.GetTempPath(), $"snapshot_{Guid.NewGuid()}.json");

            // Create
            var createRequest = new CreateIndexRequest
            {
                TenantId = tenantId,
                IndexName = indexName,
                Dimension = 10,
                Metric = "L2"
            };
            await _client.PostAsync("/v1/indexes",
                new StringContent(JsonSerializer.Serialize(createRequest), Encoding.UTF8, "application/json"));

            // Snapshot
            var snapshotReq = new SnapshotRequest { Path = snapshotPath };
            var snapResponse = await _client.PostAsync($"/v1/indexes/{tenantId}/{indexName}/snapshot",
                 new StringContent(JsonSerializer.Serialize(snapshotReq), Encoding.UTF8, "application/json"));

            snapResponse.EnsureSuccessStatusCode();
            Assert.True(File.Exists(snapshotPath));

            // Load (into same index for simplicity, or we could delete and reload)
            var loadReq = new SnapshotRequest { Path = snapshotPath };
            var loadResponse = await _client.PostAsync($"/v1/indexes/{tenantId}/{indexName}/load",
                 new StringContent(JsonSerializer.Serialize(loadReq), Encoding.UTF8, "application/json"));

            loadResponse.EnsureSuccessStatusCode();

            // Cleanup
            if (File.Exists(snapshotPath)) File.Delete(snapshotPath);
        }

        [Fact]
        public async Task Build_ReturnsOk()
        {
            var tenantId = "tenant4";
            var indexName = "index4";
            // Create
            var createRequest = new CreateIndexRequest
            {
                TenantId = tenantId,
                IndexName = indexName,
                Dimension = 10,
                Metric = "L2"
            };
            await _client.PostAsync("/v1/indexes",
                new StringContent(JsonSerializer.Serialize(createRequest), Encoding.UTF8, "application/json"));

            var response = await _client.PostAsync($"/v1/indexes/{tenantId}/{indexName}/build", null);
            response.EnsureSuccessStatusCode();
        }

        [Fact]
        public async Task Snapshot_EmptyPath_ReturnsBadRequest()
        {
            var tenantId = "tenant5";
            var indexName = "index5";

            // Create
            var createRequest = new CreateIndexRequest
            {
                TenantId = tenantId,
                IndexName = indexName,
                Dimension = 10,
                Metric = "L2"
            };
            await _client.PostAsync("/v1/indexes",
                new StringContent(JsonSerializer.Serialize(createRequest), Encoding.UTF8, "application/json"));

            // Snapshot with empty path
            var snapshotReq = new SnapshotRequest { Path = "" };
            var response = await _client.PostAsync($"/v1/indexes/{tenantId}/{indexName}/snapshot",
                 new StringContent(JsonSerializer.Serialize(snapshotReq), Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Load_EmptyPath_ReturnsBadRequest()
        {
            var tenantId = "tenant6";
            var indexName = "index6";

            // Create
            var createRequest = new CreateIndexRequest
            {
                TenantId = tenantId,
                IndexName = indexName,
                Dimension = 10,
                Metric = "L2"
            };
            await _client.PostAsync("/v1/indexes",
                new StringContent(JsonSerializer.Serialize(createRequest), Encoding.UTF8, "application/json"));

            // Load with empty path
            var loadReq = new SnapshotRequest { Path = "" };
            var response = await _client.PostAsync($"/v1/indexes/{tenantId}/{indexName}/load",
                 new StringContent(JsonSerializer.Serialize(loadReq), Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task MissingApiKey_ReturnsUnauthorized()
        {
            using var client = _factory.CreateClient();
            var response = await client.GetAsync("/v1/health");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Build_AutoSyncsCentroidsToSemanticClusterRegistry()
        {
            var tenantId = "tenant_centroid_sync";
            var indexName = "idx_centroid_sync";

            // Create index with IVF_FLAT algorithm
            var createRequest = new CreateIndexRequest
            {
                TenantId = tenantId,
                IndexName = indexName,
                Dimension = 2,
                Metric = "L2",
                Algorithm = "IVF_FLAT",
                Parameters = new System.Collections.Generic.Dictionary<string, object> { ["nlist"] = 2 }
            };
            var createResp = await _client.PostAsync("/v1/indexes",
                new StringContent(JsonSerializer.Serialize(createRequest), Encoding.UTF8, "application/json"));
            createResp.EnsureSuccessStatusCode();

            // Add vectors directly via the registry
            using var scope = _factory.Services.CreateScope();
            var registry = scope.ServiceProvider.GetRequiredService<VectorIndexRegistry>();
            var index = registry.GetOrCreate(tenantId, indexName, 2, Pyrope.GarnetServer.Vector.VectorMetric.L2);
            index.Add("v1", new float[] { 0.1f, 0.1f });
            index.Add("v2", new float[] { 0.2f, 0.2f });
            index.Add("v3", new float[] { 10f, 10f });
            index.Add("v4", new float[] { 11f, 11f });

            // Build via HTTP API
            var buildResp = await _client.PostAsync($"/v1/indexes/{tenantId}/{indexName}/build", null);
            buildResp.EnsureSuccessStatusCode();

            // Verify centroids were auto-synced to SemanticClusterRegistry
            var clusterRegistry = scope.ServiceProvider.GetRequiredService<SemanticClusterRegistry>();
            var centroids = clusterRegistry.GetCentroids(tenantId, indexName);

            Assert.NotNull(centroids);
            Assert.True(centroids!.Count > 0, "SemanticClusterRegistry should have centroids after Build");
        }
    }
}
