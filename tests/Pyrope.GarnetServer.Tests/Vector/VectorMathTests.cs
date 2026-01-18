using System;
using System.Linq;
using Xunit;
using Pyrope.GarnetServer.Vector;

namespace Pyrope.GarnetServer.Tests.Vector
{
    public class VectorMathTests
    {
        [Fact]
        public void DotProduct_MatchesReference()
        {
            var a = new float[] { 1, 2, 3, 4, 5 };
            var b = new float[] { 2, 3, 4, 5, 6 };

            var expected = 0f;
            for (int i = 0; i < a.Length; i++) expected += a[i] * b[i];

            var actual = VectorMath.DotProduct(a, b);
            Assert.Equal(expected, actual, 1e-6f);
        }

        [Fact]
        public void L2Squared_MatchesReference()
        {
            var a = new float[] { 1, 2, 3, 4, 5 };
            var b = new float[] { 2, 3, 4, 5, 6 };

            var expected = 0f;
            for (int i = 0; i < a.Length; i++)
            {
                var diff = a[i] - b[i];
                expected += diff * diff;
            }

            var actual = VectorMath.L2Squared(a, b);
            Assert.Equal(expected, actual, 1e-6f);
        }

        [Fact]
        public void ComputeNorm_MatchesReference()
        {
            var a = new float[] { 1, 2, 3, 4, 5 };

            var sum = 0f;
            for (int i = 0; i < a.Length; i++) sum += a[i] * a[i];
            var expected = (float)Math.Sqrt(sum);

            var actual = VectorMath.ComputeNorm(a);
            Assert.Equal(expected, actual, 1e-6f);
        }

        [Fact]
        public void Cosine_MatchesReference()
        {
            var a = new float[] { 1, 0, 0 };
            var b = new float[] { 0, 1, 0 }; // 90 deg
            Assert.Equal(0f, VectorMath.Cosine(a, b), 1e-6f);

            var c = new float[] { 1, 2, 3 };
            var dot = VectorMath.DotProduct(c, c);
            var norm = VectorMath.ComputeNorm(c);
            var expected = dot / (norm * norm); // 1.0
            Assert.Equal(expected, VectorMath.Cosine(c, c), 1e-6f);
        }

        [Fact]
        public void LargeVector_MatchesReference()
        {
            var dim = 1024 + 13; // Check boundary conditions (not multiple of register size)
            var a = Enumerable.Range(0, dim).Select(i => (float)i * 0.001f).ToArray();
            var b = Enumerable.Range(0, dim).Select(i => (float)i * 0.0005f).ToArray();

            // Dot
            float expectedDot = 0f;
            for (int i = 0; i < dim; i++) expectedDot += a[i] * b[i];
            Assert.Equal(expectedDot, VectorMath.DotProduct(a, b), 1.0f); // Relaxed for large sum precision

            // L2
            float expectedL2 = 0f;
            for (int i = 0; i < dim; i++) { var d = a[i] - b[i]; expectedL2 += d * d; }
            Assert.Equal(expectedL2, VectorMath.L2Squared(a, b), 1.0f); // Relaxed for large sum precision
        }

        [Fact]
        public void DotProduct_ThrowsOnDimensionMismatch()
        {
            var a = new float[10];
            var b = new float[11];
            Assert.Throws<ArgumentException>(() => VectorMath.DotProduct(a, b));
        }

        [Fact]
        public void L2Squared_ThrowsOnDimensionMismatch()
        {
            var a = new float[10];
            var b = new float[11];
            Assert.Throws<ArgumentException>(() => VectorMath.L2Squared(a, b));
        }

        [Fact]
        public void Cosine_ThrowsOnDimensionMismatch()
        {
            var a = new float[10];
            var b = new float[11];
            Assert.Throws<ArgumentException>(() => VectorMath.Cosine(a, b));
        }
        [Fact]
        public void DotProductUnsafe_MatchesReference()
        {
            var dim = 1024 + 13; // Unaligned
            var a = Enumerable.Range(0, dim).Select(i => (float)i * 0.001f).ToArray();
            var b = Enumerable.Range(0, dim).Select(i => (float)i * 0.0005f).ToArray();

            var expected = VectorMath.DotProduct(a, b);
            var actual = VectorMath.DotProductUnsafe(a, b);
            Assert.Equal(expected, actual, 1e-4f);
        }

        [Fact]
        public void L2SquaredUnsafe_MatchesReference()
        {
            var dim = 1024 + 13; // Unaligned
            var a = Enumerable.Range(0, dim).Select(i => (float)i * 0.001f).ToArray();
            var b = Enumerable.Range(0, dim).Select(i => (float)i * 0.0005f).ToArray();

            var expected = VectorMath.L2Squared(a, b);
            var actual = VectorMath.L2SquaredUnsafe(a, b);
            Assert.Equal(expected, actual, 1e-4f);
        }
    }
}
