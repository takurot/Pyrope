using System;
using Pyrope.GarnetServer.Vector;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Vector
{
    public class BruteForceVectorIndexTests
    {
        [Fact]
        public void Search_WithCosineMetric_ReturnsClosestVector()
        {
            var index = new BruteForceVectorIndex(2, VectorMetric.Cosine);
            index.Add("a", new float[] { 1f, 0f });
            index.Add("b", new float[] { 0f, 1f });

            var results = index.Search(new float[] { 1f, 0.1f }, 1);

            Assert.Single(results);
            Assert.Equal("a", results[0].Id);
        }

        [Fact]
        public void Upsert_OverwritesExistingVector()
        {
            var index = new BruteForceVectorIndex(2, VectorMetric.InnerProduct);
            index.Add("a", new float[] { 1f, 0f });

            index.Upsert("a", new float[] { 0f, 2f });
            var results = index.Search(new float[] { 0f, 1f }, 1);

            Assert.Equal("a", results[0].Id);
            Assert.True(results[0].Score > 1f);
        }

        [Fact]
        public void Delete_RemovesVector()
        {
            var index = new BruteForceVectorIndex(2, VectorMetric.L2);
            index.Add("a", new float[] { 1f, 1f });

            var removed = index.Delete("a");
            var results = index.Search(new float[] { 1f, 1f }, 1);

            Assert.True(removed);
            Assert.Empty(results);
        }

        [Fact]
        public void Add_WithWrongDimension_Throws()
        {
            var index = new BruteForceVectorIndex(2, VectorMetric.L2);
            Assert.Throws<ArgumentException>(() => index.Add("a", new float[] { 1f }));
        }
    }
}
