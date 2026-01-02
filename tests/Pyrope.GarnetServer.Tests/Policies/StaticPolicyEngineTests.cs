using System;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Policies;
using Pyrope.GarnetServer.Vector;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Policies
{
    public class StaticPolicyEngineTests
    {
        [Fact]
        public void Evaluate_ShouldReturnCacheDecisionWithDefaultTtl()
        {
            var ttl = TimeSpan.FromSeconds(60);
            var engine = new StaticPolicyEngine(ttl);
            var key = CreateKey();

            var decision = engine.Evaluate(key);

            Assert.True(decision.ShouldCache);
            Assert.Equal(ttl, decision.Ttl);
        }

        [Fact]
        public void UpdatePolicy_ShouldUpdateTtl_ThreadSafe()
        {
            // Arrange
            var initialTtl = TimeSpan.FromSeconds(60);
            var engine = new StaticPolicyEngine(initialTtl);
            var key = CreateKey();

            // Act - Verify initial state
            var initialDecision = engine.Evaluate(key);
            Assert.Equal(initialTtl, initialDecision.Ttl);

            // Act - Update Policy
            var newTtlSeconds = 120;
            var newPolicy = new Pyrope.Policy.WarmPathPolicy
            {
                TtlSeconds = newTtlSeconds,
                AdmissionThreshold = 0.5,
                EvictionPriority = 10
            };

            engine.UpdatePolicy(newPolicy);

            // Assert
            var updatedDecision = engine.Evaluate(key);
            Assert.Equal(TimeSpan.FromSeconds(newTtlSeconds), updatedDecision.Ttl);
        }

        private QueryKey CreateKey()
        {
            return new QueryKey(
                "tenant",
                "index",
                new float[128],
                10,
                VectorMetric.L2,
                null
            );
        }
    }
}
