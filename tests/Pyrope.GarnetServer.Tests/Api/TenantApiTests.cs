using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pyrope.GarnetServer;
using Pyrope.GarnetServer.Controllers;
using Pyrope.GarnetServer.Model;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Api
{
    public class TenantApiTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private const string AdminApiKey = "test-admin-key";
        private readonly HttpClient _client;

        public TenantApiTests(WebApplicationFactory<Program> factory)
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
        public async Task CreateTenant_ReturnsCreated()
        {
            var request = new CreateTenantRequest
            {
                TenantId = "tenant_api_1",
                Quotas = new TenantQuota { MaxQps = 100 }
            };

            var response = await _client.PostAsync("/v1/tenants",
                new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        [Fact]
        public async Task GetQuotas_ReturnsOk()
        {
            var tenantId = "tenant_api_2";
            var createRequest = new CreateTenantRequest { TenantId = tenantId };
            await _client.PostAsync("/v1/tenants",
                new StringContent(JsonSerializer.Serialize(createRequest), Encoding.UTF8, "application/json"));

            var response = await _client.GetAsync($"/v1/tenants/{tenantId}/quotas");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var quotas = JsonSerializer.Deserialize<TenantQuota>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            Assert.NotNull(quotas);
        }

        [Fact]
        public async Task UpdateQuotas_ReturnsOk()
        {
            var tenantId = "tenant_api_3";
            var createRequest = new CreateTenantRequest { TenantId = tenantId };
            await _client.PostAsync("/v1/tenants",
                new StringContent(JsonSerializer.Serialize(createRequest), Encoding.UTF8, "application/json"));

            var updateRequest = new TenantQuota { MaxQps = 200, CacheMemoryMb = 512 };
            var response = await _client.PutAsync($"/v1/tenants/{tenantId}/quotas",
                new StringContent(JsonSerializer.Serialize(updateRequest), Encoding.UTF8, "application/json"));

            response.EnsureSuccessStatusCode();
        }

        [Fact]
        public async Task UpdateQuotas_UnknownTenant_ReturnsNotFound()
        {
            var updateRequest = new TenantQuota { MaxQps = 10 };
            var response = await _client.PutAsync("/v1/tenants/missing/quotas",
                new StringContent(JsonSerializer.Serialize(updateRequest), Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }
}
