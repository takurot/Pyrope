using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pyrope.GarnetServer;
using Pyrope.GarnetServer.Model;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Api
{
    public class CacheApiTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private const string AdminApiKey = "test-admin-key";
        private readonly HttpClient _client;

        public CacheApiTests(WebApplicationFactory<Program> factory)
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
        public async Task GetPolicies_ReturnsOk()
        {
            var response = await _client.GetAsync("/v1/cache/policies");
            response.EnsureSuccessStatusCode();
        }

        [Fact]
        public async Task UpdatePolicies_ReturnsOk()
        {
            var request = new CachePolicyConfig { EnableCache = true, DefaultTtlSeconds = 30 };
            var response = await _client.PutAsync("/v1/cache/policies",
                new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"));

            response.EnsureSuccessStatusCode();
        }

        [Fact]
        public async Task Flush_ReturnsOk()
        {
            var response = await _client.PostAsync("/v1/cache/flush", null);
            response.EnsureSuccessStatusCode();
        }

        [Fact]
        public async Task Invalidate_MissingFields_ReturnsBadRequest()
        {
            var response = await _client.PostAsync("/v1/cache/invalidate",
                new StringContent("{}", Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }
}
