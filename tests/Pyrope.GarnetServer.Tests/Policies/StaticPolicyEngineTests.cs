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
