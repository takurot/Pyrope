using System;
using System.Linq;
using Pyrope.GarnetServer.Vector;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Vector
{
    public class HnswVectorIndexTests
    {
        [Fact]
        public void AddAndSearch_FindsExactMatch()
        {
            var index = new HnswVectorIndex(2, VectorMetric.L2, m: 16, efConstruction: 200, efSearch: 10);
            var vec = new float[] { 1.0f, 0.0f };
            index.Add("1", vec);

            var results = index.Search(vec, 1);

            Assert.Single(results);
            Assert.Equal("1", results[0].Id);
            Assert.Equal(0.0f, results[0].Score, 5); // L2 distance should be 0 for exact match
        }

        [Fact]
        public void AddMultiple_SearchNearest()
        {
            var index = new HnswVectorIndex(2, VectorMetric.L2);
            index.Add("1", new float[] { 1.0f, 0.0f });
            index.Add("2", new float[] { 0.0f, 1.0f });
            index.Add("3", new float[] { -1.0f, 0.0f });

            // Query near "2"
            var query = new float[] { 0.1f, 0.9f };
            var results = index.Search(query, 1);

            Assert.Single(results);
            Assert.Equal("2", results[0].Id);
        }

        [Fact]
        public void AddDuplicateId_UpdatesExisting()
        {
            var index = new HnswVectorIndex(2, VectorMetric.L2);
            index.Add("1", new float[] { 1.0f, 0.0f });

            // Search should find it
            var initial = index.Search(new float[] { 1.0f, 0.0f }, 1);
            Assert.Equal("1", initial[0].Id);
            Assert.Equal(0.0f, initial[0].Score, 5);

            // Update "1" to be at {0, 1}
            index.Add("1", new float[] { 0.0f, 1.0f });

            // Search at old location (should not find matching simple distance 0)
            var oldLoc = index.Search(new float[] { 1.0f, 0.0f }, 1);
            // Distance of {1,0} to {0,1} is 1+1=2. L2 Score is -2.
            Assert.Equal("1", oldLoc[0].Id);
            Assert.Equal(-2.0f, oldLoc[0].Score, 1);

            // Search at new location
            var newLoc = index.Search(new float[] { 0.0f, 1.0f }, 1);
            Assert.Equal("1", newLoc[0].Id);
            Assert.Equal(0.0f, newLoc[0].Score, 5);
        }

        [Fact]
        public void Delete_RemovesItemFromSearch()
        {
            var index = new HnswVectorIndex(2, VectorMetric.L2);
            index.Add("1", new float[] { 1.0f, 0.0f });
            index.Add("2", new float[] { 0.0f, 1.0f });

            // Verify both exist
            var before = index.Search(new float[] { 0.0f, 0.0f }, 10);
            Assert.Equal(2, before.Count);

            // Delete "1"
            bool deleted = index.Delete("1");
            Assert.True(deleted);

            // Verify "1" is gone
            var after = index.Search(new float[] { 0.0f, 0.0f }, 10);
            Assert.Single(after);
            Assert.Equal("2", after[0].Id);

            // Delete non-existent
            Assert.False(index.Delete("999"));
        }

        [Fact]
        public void Cosine_NormalizesInputAutomatically()
        {
            var index = new HnswVectorIndex(2, VectorMetric.Cosine);

            // Add unnormalized vector {10, 0} -> Should become {1, 0}
            index.Add("1", new float[] { 10.0f, 0.0f });

            // Search with unnormalized vector {0, 5} -> Should become {0, 1}
            // Dot product of {1,0} and {0,1} is 0. Distance = 1 - 0 = 1. Score = 1 - 1 = 0.
            var res1 = index.Search(new float[] { 0.0f, 5.0f }, 1);
            Assert.Equal("1", res1[0].Id);
            Assert.Equal(0.0f, res1[0].Score, 5); // Score = 1 - Dist(=1) => 0

            // Search with unnormalized vector {5, 0} -> Should become {1, 0}
            // Dot product {1,0}*{1,0}=1. Distance = 1-1=0. Score = 1-0=1.
            var res2 = index.Search(new float[] { 5.0f, 0.0f }, 1);
            Assert.Equal("1", res2[0].Id);
            Assert.Equal(1.0f, res2[0].Score, 5);
        }

        [Fact]
        public void Constructor_ValidatesParameters()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new HnswVectorIndex(0, VectorMetric.L2));
            Assert.Throws<ArgumentOutOfRangeException>(() => new HnswVectorIndex(2, VectorMetric.L2, m: 1));
        }

        [Fact]
        public void Add_ValidatesInput()
        {
            var index = new HnswVectorIndex(2, VectorMetric.L2);
            Assert.Throws<ArgumentNullException>(() => index.Add("1", null!));
            Assert.Throws<ArgumentException>(() => index.Add("1", new float[] { 1f })); // Wrong dim
        }
    }
}
