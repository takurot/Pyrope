using System;
using System.Collections.Generic;
using Pyrope.GarnetServer.Vector;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Vector
{
    public class KMeansUtilsTests
    {
        [Fact]
        public void Train_WithNullData_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                KMeansUtils.Train(null!, 2, 4, VectorMetric.L2));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void Train_WithInvalidDimension_ThrowsArgumentOutOfRangeException(int dim)
        {
            var data = new List<float[]> { new float[] { 1f, 2f } };
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                KMeansUtils.Train(data, 1, dim, VectorMetric.L2));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void Train_WithNonPositiveK_ThrowsArgumentOutOfRangeException(int k)
        {
            var data = new List<float[]> { new float[] { 1f, 2f } };
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                KMeansUtils.Train(data, k, 2, VectorMetric.L2));
        }

        [Fact]
        public void FindNearestCentroid_WithNullVector_ThrowsArgumentNullException()
        {
            var centroids = new List<float[]> { new float[] { 1f, 0f } };
            var norms = new List<float> { 1f };
            Assert.Throws<ArgumentNullException>(() =>
                KMeansUtils.FindNearestCentroid(null!, centroids, norms, VectorMetric.L2));
        }

        [Fact]
        public void FindNearestCentroid_WithNullOrEmptyCentroids_ThrowsArgumentException()
        {
            var vec = new float[] { 1f, 0f };
            Assert.Throws<ArgumentException>(() =>
                KMeansUtils.FindNearestCentroid(vec, null!, new List<float>(), VectorMetric.L2));

            Assert.Throws<ArgumentException>(() =>
                KMeansUtils.FindNearestCentroid(vec, new List<float[]>(), new List<float>(), VectorMetric.L2));
        }

        [Fact]
        public void FindNearestCentroid_WhenScoresAreLargeNegative_FindsClosestCentroid()
        {
            // Vector far away so distance squared is huge
            var vec = new float[] { 1000f, 1000f };
            var c1 = new float[] { 0f, 0f };
            var c2 = new float[] { 900f, 900f }; // much closer to vec
            var centroids = new List<float[]> { c1, c2 };
            var norms = new List<float> { 0f, 0f };

            int best = KMeansUtils.FindNearestCentroid(vec, centroids, norms, VectorMetric.L2);
            Assert.Equal(1, best);
        }
    }
}
