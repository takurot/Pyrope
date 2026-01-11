using System.Collections.Concurrent;
using System.Collections.Generic;
using Pyrope.GarnetServer.Utils;
using Pyrope.GarnetServer.Vector;

namespace Pyrope.GarnetServer.Services
{
    public class SemanticClusterRegistry
    {
        // Key: "tenant:index" -> Centroids list
        private readonly ConcurrentDictionary<string, List<float[]>> _centroids = new();

        public void UpdateCentroids(string tenantId, string indexName, List<float[]> centroids)
        {
            var key = KeyUtils.GetIndexKey(tenantId, indexName);
            _centroids[key] = centroids; // Replace entirely
        }

        public List<float[]>? GetCentroids(string tenantId, string indexName)
        {
            var key = KeyUtils.GetIndexKey(tenantId, indexName);
            if (_centroids.TryGetValue(key, out var list))
            {
                return list;
            }
            return null;
        }

        public (int ClusterId, float Score) FindNearestCluster(string tenantId, string indexName, float[] query, VectorMetric metric)
        {
            var centroids = GetCentroids(tenantId, indexName);
            if (centroids == null || centroids.Count == 0) return (-1, 0f);

            int bestIndex = -1;
            float bestScore = metric == VectorMetric.L2 ? float.MaxValue : float.MinValue;

            for (int i = 0; i < centroids.Count; i++)
            {
                float score = VectorMath.CalculateDistance(query, centroids[i], metric);
                
                if (metric == VectorMetric.L2)
                {
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestIndex = i;
                    }
                }
                else
                {
                    // IP/Cosine: higher is better
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestIndex = i;
                    }
                }
            }
            return (bestIndex, bestScore);
        }

        public bool TryGetCentroid(string tenantId, string indexName, int clusterId, out float[]? centroid)
        {
            centroid = null;
            var list = GetCentroids(tenantId, indexName);
            if (list == null || clusterId < 0 || clusterId >= list.Count)
            {
                return false;
            }
            centroid = list[clusterId];
            return true;
        }
    }

    public static class VectorMath
    {
        // Simple shim, ideally use SIMD accelerated version from VectorIndex
        public static float CalculateDistance(float[] a, float[] b, VectorMetric metric)
        {
            if (metric == VectorMetric.L2)
            {
                float sum = 0;
                for (int i = 0; i < a.Length; i++)
                {
                    float d = a[i] - b[i];
                    sum += d * d;
                }
                return sum;
            }
            else // IP or Cosine (assuming normalized for cosine)
            {
                float dot = 0;
                for (int i = 0; i < a.Length; i++) dot += a[i] * b[i];
                return dot;
            }
        }
    }
}
