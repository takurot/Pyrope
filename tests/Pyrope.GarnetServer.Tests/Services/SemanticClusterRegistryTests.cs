using System;
using System.Collections.Generic;
using Pyrope.GarnetServer.Services;
using Pyrope.GarnetServer.Vector;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Services
{
    public class SemanticClusterRegistryTests
    {
        [Fact]
        public void RecordWrite_IncrementsHeat_And_ShortensTTL()
        {
            var time = new MockTimeProvider();
            var registry = new SemanticClusterRegistry(time);
            
            // Setup centroids
            var centroids = new List<float[]> { new float[] { 1.0f, 0.0f }, new float[] { 0.0f, 1.0f } };
            registry.UpdateCentroids("t1", "idx1", centroids);

            // Base TTL: 60s
            var baseTTL = TimeSpan.FromSeconds(60);

            // Initially cold
            var ttl = registry.GetRecommendedTTL("t1", "idx1", 0, baseTTL);
            Assert.Equal(baseTTL, ttl);

            // Record writes to Cluster 0 (vector [1,0] matches centroid 0 [1,0])
            for (int i = 0; i < 11; i++) // Threshold is 10
            {
                registry.RecordWrite("t1", "idx1", new float[] { 1.0f, 0.0f }, VectorMetric.L2);
            }

            // Now hot
            ttl = registry.GetRecommendedTTL("t1", "idx1", 0, baseTTL);
            Assert.True(ttl < baseTTL, "TTL should be reduced");
            Assert.Equal(TimeSpan.FromSeconds(6), ttl); // 10% of 60s = 6s

            // Cluster 1 should still be cold
            var ttl2 = registry.GetRecommendedTTL("t1", "idx1", 1, baseTTL);
            Assert.Equal(baseTTL, ttl2);
        }

        [Fact]
        public void Heat_Decays_AfterWindow()
        {
            var time = new MockTimeProvider();
            var registry = new SemanticClusterRegistry(time);
            
            registry.UpdateCentroids("t1", "idx1", new List<float[]> { new float[] { 1f } });

            // Heat up
            for (int i = 0; i < 15; i++)
            {
                registry.RecordWrite("t1", "idx1", new float[] { 1f }, VectorMetric.L2);
            }
            Assert.True(registry.GetRecommendedTTL("t1", "idx1", 0, TimeSpan.FromSeconds(60)) < TimeSpan.FromSeconds(60));

            // Move time forward by 61 seconds
            time.Advance(61);

            // Should be cold again (lazy reset on next access check or write?
            // GetRecommendedTTL checks window: if (now - WindowStart <= 60). 
            // If advanced 61s, then `now - WindowStart` > 60. Condition fails. Returns baseTTL.
            
            var ttl = registry.GetRecommendedTTL("t1", "idx1", 0, TimeSpan.FromSeconds(60));
            Assert.Equal(TimeSpan.FromSeconds(60), ttl);
        }
    }

    public class MockTimeProvider : ITimeProvider
    {
        private long _seconds = 1000000;

        public long GetUnixTimeSeconds()
        {
            return _seconds;
        }

        public void Advance(long seconds)
        {
            _seconds += seconds;
        }
    }
}
