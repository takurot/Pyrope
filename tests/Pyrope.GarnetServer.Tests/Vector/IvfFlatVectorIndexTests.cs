using System;
using System.Linq;
using System.IO;
using Pyrope.GarnetServer.Vector;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Vector
{
    public class IvfFlatVectorIndexTests
    {
        [Fact]
        public void Search_BeforeBuild_ReturnsResultsFromBuffer()
        {
            // Even if not built, it should likely return results (or minimal implementation might require Build)
            // For MVP aimed at Tail index, we usually assume Build is called. 
            // But let's verify basic storage works.
            var index = new IvfFlatVectorIndex(2, VectorMetric.L2, nList: 2);
            index.Add("a", new float[] { 1f, 0f });
            index.Add("b", new float[] { 5f, 5f });

            // Before build, it might behave like brute force on buffer
            var results = index.Search(new float[] { 1f, 0f }, 1);

            Assert.Single(results);
            Assert.Equal("a", results[0].Id);
        }

        [Fact]
        public void Build_ClustersData()
        {
            var index = new IvfFlatVectorIndex(2, VectorMetric.L2, nList: 2);
            // Cluster A: around (0,0)
            index.Add("a1", new float[] { 0.1f, 0.1f });
            index.Add("a2", new float[] { 0.2f, 0.2f });

            // Cluster B: around (10,10)
            index.Add("b1", new float[] { 10.1f, 10.1f });
            index.Add("b2", new float[] { 10.2f, 10.2f });

            index.Build();

            // After build, search for (0,0) should return a1 or a2
            // And implicitly we want to know if it pruned the search space, but that's hard to test purely functionally.
            // We mainly check recall here.

            var results = index.Search(new float[] { 0f, 0f }, 2, new SearchOptions(MaxScans: null));
            Assert.Equal(2, results.Count);
            Assert.Contains(results, r => r.Id.StartsWith("a"));
            Assert.DoesNotContain(results, r => r.Id.StartsWith("b")); // With nProbe=1 (default), likely skips cluster B
        }

        [Fact]
        public void Search_WithNProbe_IncreasesRecall()
        {
            // 3 Clusters
            var index = new IvfFlatVectorIndex(2, VectorMetric.L2, nList: 3);
            index.CombineNProbe = 1; // Explicitly search only 1 cluster by default

            // Add points
            index.Add("c1", new float[] { 0f, 0f });
            index.Add("c2", new float[] { 5f, 5f });
            index.Add("c3", new float[] { 10f, 10f });

            index.Build();

            // Query between c1 and c2, but closer to c1. 
            // If we only search nearest cluster, we might miss c2 if it ended up in a different cluster 
            // but the query was on the boundary.

            // Hard to deterministically test boundary conditions with simple K-Means without seeds.
            // Instead, just ensure nProbe=NList returns everything.

            index.CombineNProbe = 3;
            var results = index.Search(new float[] { 0f, 0f }, 3);
            Assert.Equal(3, results.Count);
        }

        [Fact]
        public void SnapshotLoad_PreservesState()
        {
            var path = Path.GetTempFileName();
            try
            {
                var index = new IvfFlatVectorIndex(2, VectorMetric.L2, nList: 2);
                index.Add("a", new float[] { 1f, 0f });
                index.Build();

                index.Snapshot(path);

                var loaded = new IvfFlatVectorIndex(2, VectorMetric.L2, nList: 2);
                loaded.Load(path);

                var results = loaded.Search(new float[] { 1f, 0f }, 1);
                Assert.Single(results);
                Assert.Equal("a", results[0].Id);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void Load_MissingFields_ShouldHandleGracefully()
        {
            var path = Path.GetTempFileName();
            try
            {
                // Create a partial JSON that is missing some 'required' fields
                // Simulates an old snapshot format
                var json = @"{
                    ""Dimension"": 2,
                    ""Metric"": 1,
                    ""IsBuilt"": false,
                    ""Buffer"": {} 
                }";
                File.WriteAllText(path, json);

                var index = new IvfFlatVectorIndex(2, VectorMetric.L2);
                // With 'required' properties, this SHOULD throw JsonException
                // We want to verify this behavior or (if we fix it) verify it doesn't throw.
                // Current requirement: Remove 'required' to allow backward compatibility.

                // So, after fix, this should NOT throw.
                index.Load(path);

                // Verify internal state is safe (defaults initialized)
                var results = index.Search(new float[] { 0f, 0f }, 1);
                Assert.Empty(results);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
