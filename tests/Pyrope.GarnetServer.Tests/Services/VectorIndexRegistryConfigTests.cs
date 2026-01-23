using System.Collections.Generic;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Services;
using Pyrope.GarnetServer.Vector;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Services
{
    public class VectorIndexRegistryConfigTests
    {
        [Fact]
        public void GetOrCreate_WithNListConfig_CreatesIvfWithCorrectNList()
        {
            var registry = new VectorIndexRegistry();
            var config = new IndexConfig
            {
                Dimension = 128,
                Metric = "L2",
                Algorithm = "IVF_FLAT",
                Parameters = new Dictionary<string, object> { { "nlist", 500 } }
            };

            var index = registry.GetOrCreate("tenant1", "index_ivf_500", 128, VectorMetric.L2, config);

            Assert.IsType<DeltaVectorIndex>(index);
            var delta = (DeltaVectorIndex)index;

            // Accessing private/internal members via reflection or just assuming for now?
            // Actually, we can check via GetStats() if we expose it, or check internal state via reflection.
            // Or better, just reflection to get the Tail object.

            var tailField = typeof(DeltaVectorIndex).GetField("_tail", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var tailIndex = tailField.GetValue(delta);

            Assert.IsType<IvfFlatVectorIndex>(tailIndex);
            var ivf = (IvfFlatVectorIndex)tailIndex;

            Assert.Equal(500, ivf.NList);
        }

        [Fact]
        public void GetOrCreate_NoConfig_DefaultsTo100()
        {
            var registry = new VectorIndexRegistry();
            var index = registry.GetOrCreate("tenant1", "index_default", 128, VectorMetric.L2);

            var delta = (DeltaVectorIndex)index;
            var tailField = typeof(DeltaVectorIndex).GetField("_tail", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var ivf = (IvfFlatVectorIndex)tailField.GetValue(delta);

            Assert.Equal(100, ivf.NList);
        }

        [Fact]
        public void GetOrCreate_AlgorithmHNSW_CreatesHnswIndex()
        {
            var registry = new VectorIndexRegistry();
            var config = new IndexConfig
            {
                Dimension = 128,
                Metric = "L2",
                Algorithm = "HNSW",
                Parameters = new Dictionary<string, object> { { "m", 24 }, { "ef_construction", 300 } }
            };

            var index = registry.GetOrCreate("tenant1", "index_hnsw", 128, VectorMetric.L2, config);

            // Should be Delta with Head=BruteForce, Tail=HNSW (or just HNSW if we change strategy, but for now Delta is standard)
            Assert.IsType<DeltaVectorIndex>(index);
            var delta = (DeltaVectorIndex)index;
            var tailField = typeof(DeltaVectorIndex).GetField("_tail", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var tailIndex = tailField.GetValue(delta);

            Assert.IsType<HnswVectorIndex>(tailIndex);
            var hnsw = (HnswVectorIndex)tailIndex;
            Assert.Equal(24, hnsw.M);
            Assert.Equal(300, hnsw.EfConstruction);
        }
    }
}
