using System;
using System.Linq;
using Pyrope.GarnetServer.Vector;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Vector
{
    public class IvfPqVectorIndexTests
    {
        [Fact]
        public void ProductQuantizer_TrainAndEncode_ReturnsCorrectDimensions()
        {
            // Dim=16, M=4 (Subspace dim=4), K=256
            var dim = 16;
            var m = 4;
            var k = 256;
            var pq = new ProductQuantizer(dim, m, k);

            var data = new float[100][];
            var rng = new Random(42);
            for (int i = 0; i < 100; i++)
            {
                data[i] = new float[dim];
                for (int j = 0; j < dim; j++) data[i][j] = (float)rng.NextDouble();
            }

            pq.Train(data);

            var vec = new float[dim];
            for (int j = 0; j < dim; j++) vec[j] = 0.5f;

            var encoded = pq.Encode(vec);
            Assert.Equal(m, encoded.Length);

            var decoded = pq.Decode(encoded);
            Assert.Equal(dim, decoded.Length);
        }

        [Fact]
        public void ProductQuantizer_TrainWithDataFewerThanK_AllocatesFullCodebook()
        {
            // Dim=8, M=2, K=16, but only 4 training vectors
            var dim = 8;
            var m = 2;
            var k = 16;
            var pq = new ProductQuantizer(dim, m, k);

            var data = new float[4][];
            for (int i = 0; i < 4; i++)
            {
                data[i] = new float[dim];
                Array.Fill(data[i], i * 0.25f);
            }

            pq.Train(data);

            var query = new float[dim];
            Array.Fill(query, 0.5f);

            var distTable = pq.ComputeDistanceTable(query);
            Assert.Equal(m, distTable.Length);
            for (int sub = 0; sub < m; sub++)
            {
                Assert.Equal(k, distTable[sub].Length);
            }

            var ipTable = pq.ComputeInnerProductTable(query);
            Assert.Equal(m, ipTable.Length);
            for (int sub = 0; sub < m; sub++)
            {
                Assert.Equal(k, ipTable[sub].Length);
            }
        }

        [Fact]
        public void Delete_AfterBuild_RemovesVectorFromInvertedListsAndSearch()
        {
            int dim = 8;
            var index = new IvfPqVectorIndex(dim, VectorMetric.L2, m: 2, k: 4, nList: 2);

            for (int i = 1; i <= 6; i++)
            {
                var v = new float[dim];
                Array.Fill(v, i * 1.0f);
                index.Add(i.ToString(), v);
            }

            index.Build();

            // Verify before delete
            Assert.Equal(6, index.GetStats().Count);

            // Delete after build
            bool deleted = index.Delete("3");
            Assert.True(deleted);

            // Verify count decreased
            Assert.Equal(5, index.GetStats().Count);

            // Search for vector 3's exact position: 3 must not be returned
            var query = new float[dim];
            Array.Fill(query, 3.0f);
            var results = index.Search(query, 6, new SearchOptions(NProbe: 2));

            Assert.DoesNotContain(results, r => r.Id == "3");
            Assert.Contains(results, r => r.Id == "2" || r.Id == "4");

            // Deleting again returns false
            Assert.False(index.Delete("3"));
        }

        [Fact]
        public void Rebuild_PreservesPreviouslyIndexedVectors()
        {
            int dim = 8;
            var index = new IvfPqVectorIndex(dim, VectorMetric.L2, m: 2, k: 4, nList: 2);

            // 1. Add batch 1
            for (int i = 1; i <= 4; i++)
            {
                var v = new float[dim];
                Array.Fill(v, i * 1.0f);
                index.Add(i.ToString(), v);
            }
            index.Build();

            Assert.Equal(4, index.GetStats().Count);

            // 2. Add batch 2
            for (int i = 5; i <= 8; i++)
            {
                var v = new float[dim];
                Array.Fill(v, i * 1.0f);
                index.Add(i.ToString(), v);
            }

            // Rebuild
            index.Build();

            // All 8 vectors must be present in index and searchable
            Assert.Equal(8, index.GetStats().Count);

            var query1 = new float[dim];
            Array.Fill(query1, 1.0f);
            var results1 = index.Search(query1, 8, new SearchOptions(NProbe: 2));

            for (int i = 1; i <= 8; i++)
            {
                Assert.Contains(results1, r => r.Id == i.ToString());
            }
        }

        [Fact]
        public void Search_WithInnerProductMetric_ProducesAccurateRankingAndScores()
        {
            int dim = 4;
            var index = new IvfPqVectorIndex(dim, VectorMetric.InnerProduct, m: 2, k: 4, nList: 2);

            // v1: [1, 0, 0, 0] -> dot with [1, 0, 0, 0] = 1
            // v2: [2, 0, 0, 0] -> dot with [1, 0, 0, 0] = 2
            // v3: [0.5, 0, 0, 0] -> dot with [1, 0, 0, 0] = 0.5
            // v4: [-1, 0, 0, 0] -> dot with [1, 0, 0, 0] = -1
            index.Add("v1", new float[] { 1f, 0f, 0f, 0f });
            index.Add("v2", new float[] { 2f, 0f, 0f, 0f });
            index.Add("v3", new float[] { 0.5f, 0f, 0f, 0f });
            index.Add("v4", new float[] { -1f, 0f, 0f, 0f });

            index.Build();

            var query = new float[] { 1f, 0f, 0f, 0f };
            var results = index.Search(query, 4, new SearchOptions(NProbe: 2));

            Assert.Equal(4, results.Count);
            // v2 must rank highest for InnerProduct
            Assert.Equal("v2", results[0].Id);
            // Score must be positive dot product approx 2.0
            Assert.True(results[0].Score > 1.5f, $"Expected score > 1.5, but got {results[0].Score}");

            // v4 must rank lowest
            Assert.Equal("v4", results[3].Id);
            Assert.True(results[3].Score < 0f, $"Expected score < 0, but got {results[3].Score}");
        }

        [Fact]
        public void Search_WithCosineMetric_ProducesAccurateScoresInValidRange()
        {
            int dim = 4;
            var index = new IvfPqVectorIndex(dim, VectorMetric.Cosine, m: 2, k: 4, nList: 2);

            index.Add("c1", new float[] { 1f, 0f, 0f, 0f });
            index.Add("c2", new float[] { 0f, 1f, 0f, 0f });
            index.Add("c3", new float[] { -1f, 0f, 0f, 0f });
            index.Add("c4", new float[] { 1f, 1f, 0f, 0f });

            index.Build();

            var query = new float[] { 1f, 0f, 0f, 0f };
            var results = index.Search(query, 4, new SearchOptions(NProbe: 2));

            Assert.Equal(4, results.Count);
            // Scores must be clamped in [-1.0, 1.0]
            foreach (var res in results)
            {
                Assert.InRange(res.Score, -1.0f, 1.0f);
            }

            // c1 has identical direction to query -> highest cosine (~1.0)
            Assert.Equal("c1", results[0].Id);
            Assert.True(results[0].Score > 0.8f, $"Expected cosine near 1.0, but got {results[0].Score}");

            // c3 is opposite direction -> lowest cosine (~ -1.0)
            Assert.Equal("c3", results[3].Id);
            Assert.True(results[3].Score < -0.8f, $"Expected cosine near -1.0, but got {results[3].Score}");
        }

        [Fact]
        public void GetStats_ReflectsAccurateCountAcrossLifecycle()
        {
            var index = new IvfPqVectorIndex(4, VectorMetric.L2, m: 2, k: 2, nList: 2);

            Assert.Equal(0, index.GetStats().Count);

            index.Add("1", new float[] { 1f, 0f, 0f, 0f });
            index.Add("2", new float[] { 0f, 1f, 0f, 0f });
            Assert.Equal(2, index.GetStats().Count);

            index.Build();
            Assert.Equal(2, index.GetStats().Count);

            index.Delete("1");
            Assert.Equal(1, index.GetStats().Count);

            index.Add("3", new float[] { 0f, 0f, 1f, 0f });
            Assert.Equal(2, index.GetStats().Count);

            index.Build();
            Assert.Equal(2, index.GetStats().Count);
        }

        [Fact]
        public void IvfPqVectorIndex_Dispose_DisposesLockAndRejectsOperations()
        {
            var index = new IvfPqVectorIndex(4, VectorMetric.L2, m: 2, k: 2, nList: 2);
            index.Add("1", new float[] { 1f, 0f, 0f, 0f });
            index.Dispose();

            Assert.Throws<ObjectDisposedException>(() =>
                index.Add("2", new float[] { 0f, 1f, 0f, 0f }));

            Assert.Throws<ObjectDisposedException>(() =>
                index.Search(new float[] { 1f, 0f, 0f, 0f }, 1));

            Assert.Throws<ObjectDisposedException>(() =>
                index.GetStats());
        }

        [Fact]
        public void IvfPqVectorIndex_ValidatesArguments()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new IvfPqVectorIndex(0, VectorMetric.L2, 2, 2, 2));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new IvfPqVectorIndex(4, VectorMetric.L2, 0, 2, 2));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new IvfPqVectorIndex(4, VectorMetric.L2, 2, 0, 2));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new IvfPqVectorIndex(4, VectorMetric.L2, 2, 2, 0));

            var index = new IvfPqVectorIndex(4, VectorMetric.L2, 2, 2, 2);

            Assert.Throws<ArgumentException>(() =>
                index.Add("", new float[] { 1f, 0f, 0f, 0f }));

            Assert.Throws<ArgumentNullException>(() =>
                index.Add("1", null!));

            Assert.Throws<ArgumentException>(() =>
                index.Add("1", new float[] { 1f })); // dimension mismatch

            Assert.Throws<ArgumentNullException>(() =>
                index.Search(null!, 1));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                index.Search(new float[] { 1f, 0f, 0f, 0f }, 0));
        }
    }
}
