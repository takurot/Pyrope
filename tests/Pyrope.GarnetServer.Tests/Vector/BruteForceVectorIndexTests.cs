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

        [Fact]
        public void Search_WithMaxScansZero_ReturnsEmpty()
        {
            var index = new BruteForceVectorIndex(2, VectorMetric.InnerProduct);
            index.Add("a", new float[] { 1f, 0f });
            index.Add("b", new float[] { 0f, 1f });

            var results = index.Search(new float[] { 1f, 0f }, 1, new SearchOptions(MaxScans: 0));

            Assert.Empty(results);
        }

        [Fact]
        public void Search_WithQuantization_UsesSlicedArrayPoolBuffer()
        {
            // The bug is that if Dimension is, say, 12, ArrayPool might return a buffer of size 16.
            // If the buffer is not sliced, VectorMath.L2Squared8Bit might throw an exception or compare garbage.
            var index = new BruteForceVectorIndex(12, VectorMetric.L2) { EnableQuantization = true };
            float[] vec1 = new float[12];
            vec1[0] = 1f;
            index.Add("a", vec1);

            // This should not throw if the slice logic is correct
            var results = index.Search(vec1, 1);
            Assert.Single(results);
            Assert.Equal("a", results[0].Id);
        }

        [Fact]
        public void Upsert_UpdatesQuantizationWhenDisabled()
        {
            // Bug: If EnableQuantization is false, Upsert doesn't clear the stale _quantizedVectors
            var index = new BruteForceVectorIndex(2, VectorMetric.L2);
            index.EnableQuantization = true;

            index.Add("a", new float[] { 1f, 0f }); // Creates quantized entry

            index.EnableQuantization = false;
            index.Upsert("a", new float[] { 0f, 1f }); // Upsert should overwrite quantized entry with empty

            index.EnableQuantization = true;

            // After re-enabling quantization, the quantized data for "a" is Array.Empty<byte>() because
            // it was cleared during the Upsert-while-disabled. The quantized search path skips entries
            // where qTarget.Length == 0, so the result is empty.
            // NOTE: This is expected behavior — entries upserted while quantization was disabled become
            // invisible to quantized search until they are upserted again with quantization enabled.
            // If the fix were absent (stale qTarget retained), Search would return the entry with
            // the old quantized data {1f, 0f}, regardless of L2 distance, because topK=1 always picks
            // the single available entry.
            var results = index.Search(new float[] { 0f, 1f }, 1);

            Assert.Empty(results);
        }

        [Fact]
        public void Delete_ReleasesMemoryReferences()
        {
            var index = new BruteForceVectorIndex(2, VectorMetric.L2);
            index.EnableQuantization = true;
            index.Add("a", new float[] { 1f, 1f });

            index.Delete("a");

            // To test memory leak fix, we access the internal state via reflection to ensure references are null.
            // NOTE: Field names "_vectors" and "_quantizedVectors" are hard-coded. If renamed, Assert.NotNull
            // below will fail with a confusing message — update field names here accordingly.
            var vectorsField = typeof(BruteForceVectorIndex).GetField("_vectors", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var qVectorsField = typeof(BruteForceVectorIndex).GetField("_quantizedVectors", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            var vectors = vectorsField?.GetValue(index) as System.Collections.IList;
            var qVectors = qVectorsField?.GetValue(index) as System.Collections.IList;

            Assert.NotNull(vectors);
            Assert.NotNull(qVectors);

            Assert.Null(vectors[0]);
            Assert.Null(qVectors[0]);
        }
    }
}
