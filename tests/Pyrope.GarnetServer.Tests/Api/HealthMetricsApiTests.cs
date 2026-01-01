using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pyrope.GarnetServer;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Api
{
    public class HealthMetricsApiTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly HttpClient _client;

        public HealthMetricsApiTests(WebApplicationFactory<Program> factory)
        {
            var testFactory = factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();
                });
            });
            _client = testFactory.CreateClient();
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
