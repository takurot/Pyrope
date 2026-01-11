using Microsoft.Extensions.Options;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Services;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Services
{
    public class SloGuardrailsTests
    {
        [Fact]
        public void UpdateLatencyP99_TransitionsDegradedState_WithHysteresis()
        {
            var registry = new TenantRegistry();
            registry.TryCreate("t_high", new TenantQuota { Priority = 0 }, out _, apiKey: "k1");
            registry.TryCreate("t_normal", new TenantQuota { Priority = 1 }, out _, apiKey: "k2");
            registry.TryCreate("t_low", new TenantQuota { Priority = 2 }, out _, apiKey: "k3");

            var options = Options.Create(new SloGuardrailsOptions
            {
                Enabled = true,
                TargetP99Ms = 10,
                RecoveryFactor = 0.8,
                DegradedMaxScans = 123
            });
            var guardrails = new SloGuardrails(options, registry);

            guardrails.UpdateLatencyP99(9);
            Assert.False(guardrails.IsDegraded);
            Assert.Null(guardrails.GetSearchOptions("t1", "i1"));

            guardrails.UpdateLatencyP99(11);
            Assert.True(guardrails.IsDegraded);
            Assert.Null(guardrails.GetSearchOptions("t_high", "i1")); // protected
            Assert.Equal(123, guardrails.GetSearchOptions("t_normal", "i1")!.MaxScans);
            Assert.True(guardrails.ShouldForceCacheOnly("t_low", "i1"));
            Assert.False(guardrails.ShouldForceCacheOnly("t_normal", "i1"));

            // Recovery threshold: 10 * 0.8 = 8
            guardrails.UpdateLatencyP99(9);
            Assert.True(guardrails.IsDegraded);

            guardrails.UpdateLatencyP99(8);
            Assert.False(guardrails.IsDegraded);
            Assert.Null(guardrails.GetSearchOptions("t1", "i1"));
        }
    }
}

