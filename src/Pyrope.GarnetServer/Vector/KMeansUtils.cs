using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Pyrope.GarnetServer.Vector
{
    public static class KMeansUtils
    {
        public static List<float[]> Train(List<float[]> data, int k, int dimension, VectorMetric metric, int maxIter = 10, int? seed = null)
        {
            if (data.Count == 0) return new List<float[]>();
            if (k <= 0) k = 1;
            if (k > data.Count) k = data.Count;

            var rnd = seed.HasValue ? new Random(seed.Value) : new Random();

            // Init centroids (Random sampling)
            // Note: For better init, K-Means++ is preferred, but random is OK for MVP.
            var centroids = data.OrderBy(_ => rnd.Next()).Take(k).Select(x => (float[])x.Clone()).ToList();

            for (int iter = 0; iter < maxIter; iter++)
            {
                var clusters = new List<float[]>[k];
                for (int i = 0; i < k; i++) clusters[i] = new List<float[]>();

                bool changed = false;

                // Compute centroid norms once per iter (for Cosine)
                var entropyC = centroids.Select(c => metric == VectorMetric.Cosine ? VectorMath.ComputeNorm(c) : 0f).ToList();

                // Assign
                // Parallel assignment for speed
                var assignments = new int[data.Count];
                Parallel.For(0, data.Count, i =>
                {
                    assignments[i] = FindNearestCentroid(data[i], centroids, entropyC, metric);
                });

                for (int i = 0; i < data.Count; i++)
                {
                    clusters[assignments[i]].Add(data[i]);
                }

                // Update
                for (int i = 0; i < k; i++)
                {
                    if (clusters[i].Count == 0) continue;

                    var newC = new float[dimension];
                    foreach (var vec in clusters[i])
                    {
                        for (int d = 0; d < dimension; d++) newC[d] += vec[d];
                    }
                    for (int d = 0; d < dimension; d++) newC[d] /= clusters[i].Count;

                    if (!ArraysEqual(centroids[i], newC))
                    {
                        centroids[i] = newC;
                        changed = true;
                    }
                }

                if (!changed) break;
            }

            return centroids;
        }

        public static int FindNearestCentroid(float[] vec, List<float[]> centroids, List<float> centroidNorms, VectorMetric metric)
        {
            int bestIndex = 0;
            float bestScore = float.MinValue;
            float vecNorm = metric == VectorMetric.Cosine ? VectorMath.ComputeNorm(vec) : 0f;

            for (int i = 0; i < centroids.Count; i++)
            {
                float score = metric switch
                {
                    VectorMetric.L2 => -VectorMath.L2Squared(vec, centroids[i]),
                    VectorMetric.InnerProduct => VectorMath.DotProduct(vec, centroids[i]),
                    VectorMetric.Cosine => VectorMath.Cosine(vec, centroids[i], vecNorm, centroidNorms[i]),
                    _ => 0f
                };

                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }
            return bestIndex;
        }

        private static bool ArraysEqual(float[] a, float[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (Math.Abs(a[i] - b[i]) > 1e-6) return false;
            return true;
        }
    }
}
