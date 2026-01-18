using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pyrope.GarnetServer;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Services;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Api
{
    public class BillingApiTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private const string AdminApiKey = "test-admin-key";
        private readonly HttpClient _client;
        private readonly BillingMeter _meter;
        private readonly TestCacheUsageProvider _cacheUsage;

        public BillingApiTests(WebApplicationFactory<Program> factory)
        {
            _meter = new BillingMeter(new BillingOptions { CostUnitSeconds = 2.0 });
            _cacheUsage = new TestCacheUsageProvider();

            var testFactory = factory.WithWebHostBuilder(builder =>
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
                    services.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();
                    services.RemoveAll<IBillingMeter>();
                    services.RemoveAll<ICacheUsageProvider>();
                    services.AddSingleton<IBillingMeter>(_meter);
                    services.AddSingleton<ICacheUsageProvider>(_cacheUsage);
                });
            });

            _client = testFactory.CreateClient();
            _client.DefaultRequestHeaders.Add("X-API-KEY", AdminApiKey);
        }

        [Fact]
        public async Task GetUsage_ReturnsTenantUsage()
        {
            _meter.RecordRequest("tenant-a", cacheHit: true);
            _meter.RecordRequest("tenant-a", cacheHit: false);
            _meter.RecordCompute("tenant-a", 1.5);
            _meter.RecordVectorBytes("tenant-a", 100);
            _cacheUsage.SetUsage("tenant-a", 2048);

            var response = await _client.GetAsync("/v1/billing/usage?tenantId=tenant-a");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal("tenant-a", root.GetProperty("tenantId").GetString());
            var requests = root.GetProperty("requests");
            Assert.Equal(2, requests.GetProperty("requestsTotal").GetInt32());
            Assert.Equal(1, requests.GetProperty("cacheHits").GetInt32());
            Assert.Equal(1, requests.GetProperty("cacheMisses").GetInt32());

            var compute = root.GetProperty("compute");
            Assert.Equal(1.5, compute.GetProperty("computeCostUnits").GetDouble(), 3);
            Assert.Equal(3.0, compute.GetProperty("computeSeconds").GetDouble(), 3);

            var storage = root.GetProperty("storage");
            Assert.Equal(100, storage.GetProperty("vectorStorageBytes").GetInt32());

            var cache = root.GetProperty("cache");
            Assert.Equal(2048, cache.GetProperty("cacheMemoryBytes").GetInt32());
        }

        private sealed class TestCacheUsageProvider : ICacheUsageProvider
        {
            private readonly Dictionary<string, long> _usage = new(StringComparer.Ordinal);

            public long GetTenantUsageBytes(string tenantId)
            {
                return _usage.TryGetValue(tenantId, out var value) ? value : 0;
            }

            public IReadOnlyDictionary<string, long> GetAllTenantUsageBytes()
            {
                return new Dictionary<string, long>(_usage);
            }

            public void SetUsage(string tenantId, long bytes)
            {
                _usage[tenantId] = bytes;
            }
        }
    }
}
