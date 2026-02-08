using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pyrope.GarnetServer;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Services;
using Pyrope.Policy;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Api
{
    public class AiApiTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private const string AdminApiKey = "test-admin-key";
        private readonly HttpClient _client;
        private readonly FakePolicyServiceClient _fakeClient;
        private readonly IAuditLogger _auditLogger;

        public AiApiTests(WebApplicationFactory<Program> factory)
        {
            _fakeClient = new FakePolicyServiceClient();

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
                    services.RemoveAll<PolicyService.PolicyServiceClient>();
                    services.AddSingleton<PolicyService.PolicyServiceClient>(_fakeClient);
                });
            });

            _client = testFactory.CreateClient();
            _client.DefaultRequestHeaders.Add("X-API-KEY", AdminApiKey);
            _auditLogger = testFactory.Services.GetRequiredService<IAuditLogger>();
        }

        [Fact]
        public async Task ListModels_ReturnsModelListFromSidecar()
        {
            _fakeClient.NextModelList = new ModelList
            {
                ActiveModelVersion = "20260124_120000",
                CanaryModelVersion = "20260124_130000",
                Models =
                {
                    new ModelInfo { Version = "20260124_130000", Status = "canary", EvaluationScore = 0.91 },
                    new ModelInfo { Version = "20260124_120000", Status = "active", EvaluationScore = 0.88 }
                }
            };

            var response = await _client.GetAsync("/v1/ai/models");

            response.EnsureSuccessStatusCode();
            Assert.Equal(1, _fakeClient.ListModelsCalls);

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            Assert.Equal("20260124_120000", doc.RootElement.GetProperty("activeModelVersion").GetString());
            Assert.Equal("20260124_130000", doc.RootElement.GetProperty("canaryModelVersion").GetString());
            Assert.Equal(2, doc.RootElement.GetProperty("models").GetArrayLength());
        }

        [Fact]
        public async Task DeployModel_EmptyVersion_ReturnsBadRequest()
        {
            var requestJson = JsonSerializer.Serialize(new { version = "", canary = false, canaryTenants = new string[0] });

            var response = await _client.PostAsync("/v1/ai/models/deploy", new StringContent(requestJson, Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(0, _fakeClient.DeployCalls);
        }

        [Fact]
        public async Task DeployModel_Success_RecordsAuditLog()
        {
            _fakeClient.NextDeployResponse = new DeployResponse
            {
                Status = "OK",
                Version = "20260124_130000"
            };

            var requestJson = JsonSerializer.Serialize(new { version = "20260124_130000", canary = true, canaryTenants = new[] { "tenant-a" } });
            var response = await _client.PostAsync("/v1/ai/models/deploy", new StringContent(requestJson, Encoding.UTF8, "application/json"));

            response.EnsureSuccessStatusCode();
            Assert.Equal(1, _fakeClient.DeployCalls);

            var deployEvents = _auditLogger.Query(action: AuditActions.DeployModel).ToList();
            Assert.Single(deployEvents);
            Assert.Equal("20260124_130000", deployEvents[0].ResourceId);
            Assert.True(deployEvents[0].Success);
        }

        [Fact]
        public async Task DeployModel_SidecarBusinessError_RecordsFailedAuditLog()
        {
            _fakeClient.NextDeployResponse = new DeployResponse
            {
                Status = "Error: model version not found",
                Version = ""
            };

            var requestJson = JsonSerializer.Serialize(new { version = "missing", canary = false, canaryTenants = new[] { "tenant-a" } });
            var response = await _client.PostAsync("/v1/ai/models/deploy", new StringContent(requestJson, Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(1, _fakeClient.DeployCalls);

            var deployEvents = _auditLogger.Query(action: AuditActions.DeployModel).ToList();
            Assert.Single(deployEvents);
            Assert.Equal("missing", deployEvents[0].ResourceId);
            Assert.False(deployEvents[0].Success);
        }

        private sealed class FakePolicyServiceClient : PolicyService.PolicyServiceClient
        {
            public int ListModelsCalls { get; private set; }
            public int DeployCalls { get; private set; }

            public ModelList NextModelList { get; set; } = new();
            public DeployResponse NextDeployResponse { get; set; } = new() { Status = "OK", Version = "" };

            public override AsyncUnaryCall<ModelList> ListModelsAsync(Empty request, CallOptions options)
            {
                ListModelsCalls++;
                return AsyncCall(NextModelList);
            }

            public override AsyncUnaryCall<TrainResponse> TrainModelAsync(TrainRequest request, CallOptions options)
            {
                return AsyncCall(new TrainResponse { Status = "Started", JobId = "job-1" });
            }

            public override AsyncUnaryCall<DeployResponse> DeployModelAsync(DeployRequest request, CallOptions options)
            {
                DeployCalls++;
                return AsyncCall(NextDeployResponse);
            }

            public override AsyncUnaryCall<RollbackResponse> RollbackModelAsync(RollbackRequest request, CallOptions options)
            {
                return AsyncCall(new RollbackResponse { Status = "OK", ActiveVersion = "20260124_120000" });
            }

            public override AsyncUnaryCall<EvaluationMetrics> GetEvaluationsAsync(Empty request, CallOptions options)
            {
                return AsyncCall(new EvaluationMetrics { CurrentCacheHitRate = 0.9, CurrentP99Improvement = 0.2 });
            }

            private static AsyncUnaryCall<T> AsyncCall<T>(T response)
            {
                return new AsyncUnaryCall<T>(
                    Task.FromResult(response),
                    Task.FromResult(new Metadata()),
                    () => Status.DefaultSuccess,
                    () => new Metadata(),
                    () => { });
            }
        }
    }
}
