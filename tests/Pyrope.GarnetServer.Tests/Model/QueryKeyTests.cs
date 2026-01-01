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
        [Theory]
        [InlineData(1, 5)]
        [InlineData(4, 5)]
        [InlineData(5, 5)]
        [InlineData(6, 10)]
        [InlineData(9, 10)]
        [InlineData(11, 20)]
        [InlineData(49, 50)]
        [InlineData(51, 100)]
        [InlineData(101, 101)] // No rounding above 100 (for now, or maybe just return K)
        public void RoundK_ShouldBucketCorrectly(int inputK, int expectedK)
        {
            var result = QueryKey.RoundK(inputK);
            Assert.Equal(expectedK, result);
        }
        [Fact]
        public void Equals_ShouldUseSimHash_WhenProvided()
        {
            var v1 = new float[] { 1, 0 };
            var v2 = new float[] { 0, 1 }; // Different vectors

            // Same SimHash, implies L1 match
            var k1 = new QueryKey("t1", "i1", v1, 10, VectorMetric.L2, null, simHash: 12345);
            var k2 = new QueryKey("t1", "i1", v2, 10, VectorMetric.L2, null, simHash: 12345);

            Assert.Equal(k1, k2);
            Assert.Equal(k1.GetHashCode(), k2.GetHashCode());
        }

        [Fact]
        public void Equals_ShouldFail_WhenSimHashDiffers()
        {
            var v1 = new float[] { 1, 0 };
            // Same vector, different SimHash (unlikely in real life but possible if model changes)
            var k1 = new QueryKey("t1", "i1", v1, 10, VectorMetric.L2, null, simHash: 12345);
            var k2 = new QueryKey("t1", "i1", v1, 10, VectorMetric.L2, null, simHash: 67890);

            Assert.NotEqual(k1, k2);
        }

        [Fact]
        public void Equals_ShouldFallBackToVector_WhenSimHashMissing()
        {
            var v1 = new float[] { 1, 0 };
            var v2 = new float[] { 0, 1 };

            var k1 = new QueryKey("t1", "i1", v1, 10, VectorMetric.L2, null);
            var k2 = new QueryKey("t1", "i1", v2, 10, VectorMetric.L2, null);

            Assert.NotEqual(k1, k2); // L0 behavior
        }
    }
}
