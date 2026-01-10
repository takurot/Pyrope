using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pyrope.GarnetServer;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Api
{
    public class HealthMetricsApiTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private const string AdminApiKey = "test-admin-key";
        private readonly HttpClient _client;

        public HealthMetricsApiTests(WebApplicationFactory<Program> factory)
        {
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
                });
            });
            _client = testFactory.CreateClient();
            _client.DefaultRequestHeaders.Add("X-API-KEY", AdminApiKey);
        }

        [Fact]
        public async Task Health_ReturnsOk()
        {
            var response = await _client.GetAsync("/v1/health");
            response.EnsureSuccessStatusCode();
        }

        [Fact]
        public async Task Metrics_ReturnsText()
        {
            var response = await _client.GetAsync("/v1/metrics");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            Assert.Contains("cache_hit_total", content);
        }
    }
}
