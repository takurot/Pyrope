
using System;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Pyrope.GarnetServer.Services;
using Pyrope.Policy;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Services
{
    public class PredictivePrefetcherTests
    {
        [Fact]
        public async Task RecordInteraction_ShouldTriggerReport()
        {
            var config = new Mock<IConfiguration>();
            var logger = new Mock<ILogger<PredictivePrefetcher>>();
            var client = new Mock<PolicyService.PolicyServiceClient>();

            // Setup mock to return success
            var response = new ReportClusterAccessResponse { Status = "OK" };
            var asyncCall = new AsyncUnaryCall<ReportClusterAccessResponse>(
                Task.FromResult(response),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { });

            client.Setup(c => c.ReportClusterAccessAsync(It.IsAny<ReportClusterAccessRequest>(), null, null, It.IsAny<CancellationToken>()))
                  .Returns(asyncCall);

            var service = new PredictivePrefetcher(config.Object, logger.Object, client.Object);

            service.RecordInteraction("tenant1", "index1", 100);

            // Start service
            var cts = new CancellationTokenSource();
            await service.StartAsync(cts.Token);

            // Allow some time for background loop
            await Task.Delay(500);
            await service.StopAsync(cts.Token);

            // Verify
            client.Verify(c => c.ReportClusterAccessAsync(
                It.Is<ReportClusterAccessRequest>(r => r.TenantId == "tenant1" && r.IndexName == "index1" && r.Accesses[0].ClusterId == 100),
                null, null, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }

        [Fact]
        public async Task GetPrediction_ShouldReturnCorrectId_AfterRefresh()
        {
            var config = new Mock<IConfiguration>();
            var logger = new Mock<ILogger<PredictivePrefetcher>>();
            var client = new Mock<PolicyService.PolicyServiceClient>();

            // Mock GetPrefetchRules
            var response = new GetPrefetchRulesResponse();
            response.Rules.Add(new PrefetchRule { CurrentClusterId = 10, NextClusterId = 20 });

            var asyncCall = new AsyncUnaryCall<GetPrefetchRulesResponse>(
                Task.FromResult(response),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { });

            client.Setup(c => c.GetPrefetchRulesAsync(It.IsAny<GetPrefetchRulesRequest>(), null, null, It.IsAny<CancellationToken>()))
                  .Returns(asyncCall);

            var service = new PredictivePrefetcher(config.Object, logger.Object, client.Object);

            // Must record interaction to seed the known index
            service.RecordInteraction("tenant1", "index1", 10);

            // Start service
            var cts = new CancellationTokenSource();
            await service.StartAsync(cts.Token);

            // Wait for refresh (should be immediate on start)
            await Task.Delay(500);
            await service.StopAsync(cts.Token);

            // Act
            var next = service.GetPrediction("tenant1", "index1", 10);
            var unknown = service.GetPrediction("tenant1", "index1", 99);

            // Assert
            Assert.Equal(20, next);
            Assert.Equal(-1, unknown);
        }
    }
}
