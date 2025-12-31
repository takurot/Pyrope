using System;
using System.Collections.Generic;
using Xunit;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Vector;

namespace Pyrope.GarnetServer.Tests.Model
{
    public class QueryKeyTests
    {
        [Fact]
        public void Equals_SameValues_ReturnsTrue()
        {
            var key1 = new QueryKey("tenant1", "idx1", new float[] { 1.0f, 2.0f }, 10, VectorMetric.L2, new[] { "tag1", "tag2" });
            var key2 = new QueryKey("tenant1", "idx1", new float[] { 1.0f, 2.0f }, 10, VectorMetric.L2, new[] { "tag1", "tag2" });

            Assert.Equal(key1, key2);
            Assert.Equal(key1.GetHashCode(), key2.GetHashCode());
        }

        [Fact]
        public void Equals_DifferentVector_ReturnsFalse()
        {
            var key1 = new QueryKey("tenant1", "idx1", new float[] { 1.0f, 2.0f }, 10, VectorMetric.L2, null);
            var key2 = new QueryKey("tenant1", "idx1", new float[] { 1.0f, 2.1f }, 10, VectorMetric.L2, null);

            Assert.NotEqual(key1, key2);
        }

        [Fact]
        public void Equals_DifferentTagsOrder_ReturnsTrue()
        {
            var key1 = new QueryKey("tenant1", "idx1", new float[] { 1.0f }, 10, VectorMetric.L2, new[] { "a", "b" });
            var key2 = new QueryKey("tenant1", "idx1", new float[] { 1.0f }, 10, VectorMetric.L2, new[] { "b", "a" });

            Assert.Equal(key1, key2);
            Assert.Equal(key1.GetHashCode(), key2.GetHashCode());
        }

        [Fact]
        public void Equals_DifferentTags_ReturnsFalse()
        {
            var key1 = new QueryKey("tenant1", "idx1", new float[] { 1.0f }, 10, VectorMetric.L2, new[] { "a" });
            var key2 = new QueryKey("tenant1", "idx1", new float[] { 1.0f }, 10, VectorMetric.L2, new[] { "a", "b" });

            Assert.NotEqual(key1, key2);
        }

        [Fact]
        public void Equals_NullTagsVsEmptyTags_ReturnsTrue()
        {
             var key1 = new QueryKey("tenant1", "idx1", new float[] { 1.0f }, 10, VectorMetric.L2, null);
             var key2 = new QueryKey("tenant1", "idx1", new float[] { 1.0f }, 10, VectorMetric.L2, Array.Empty<string>());

             Assert.Equal(key1, key2);
             Assert.Equal(key1.GetHashCode(), key2.GetHashCode());
        }

        [Fact]
        public void GetHashCode_Consistency()
        {
            var key = new QueryKey("tenant1", "idx1", new float[] { 1.0f, 2.0f }, 10, VectorMetric.L2, new[] { "tag1" });
            var hash1 = key.GetHashCode();
            var hash2 = key.GetHashCode();

            Assert.Equal(hash1, hash2);
        }
    }
}
