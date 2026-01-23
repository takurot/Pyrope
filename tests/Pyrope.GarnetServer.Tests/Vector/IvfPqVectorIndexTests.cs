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

            // Generate synthetic training data
            var data = new float[100][];
            var rng = new Random(42);
            for (int i = 0; i < 100; i++)
            {
                data[i] = new float[dim];
                for (int j = 0; j < dim; j++) data[i][j] = (float)rng.NextDouble();
            }

            // Train (this might be a naive mock trainer initially)
            pq.Train(data);

            // Encode a vector
            var vec = new float[dim];
            for (int j = 0; j < dim; j++) vec[j] = 0.5f;

            var encoded = pq.Encode(vec);

            Assert.Equal(m, encoded.Length);
        }

        [Fact]
        public void IvfPq_Search_ReturnsResults()
        {
            // Simple integration test
            var dim = 128;
            var index = new IvfPqVectorIndex(dim, VectorMetric.L2, m: 16, k: 256, nList: 4);

            var rng = new Random(123);
            // Add some vectors
            for (int i = 0; i < 100; i++)
            {
                var v = new float[dim];
                for (int j = 0; j < dim; j++) v[j] = (float)rng.NextDouble();
                index.Add(i.ToString(), v);
            }

            // Build (trains PQ and IVF)
            index.Build();

            // Search
            var query = new float[dim];
            for (int j = 0; j < dim; j++) query[j] = 0.5f;

            var results = index.Search(query, 5);

            Assert.NotEmpty(results);
            Assert.Equal(5, results.Count);
        }
    }
}
