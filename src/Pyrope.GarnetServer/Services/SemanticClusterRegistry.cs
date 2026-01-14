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

        // Key: "tenant:index" -> (ClusterId -> HeatState)
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<int, HeatState>> _writeHeat = new();
        private readonly ITimeProvider _timeProvider;

        public SemanticClusterRegistry(ITimeProvider? timeProvider = null)
        {
            _timeProvider = timeProvider ?? new SystemTimeProvider();
        }

        public void UpdateCentroids(string tenantId, string indexName, List<float[]> centroids)
        {
            var key = KeyUtils.GetIndexKey(tenantId, indexName);
            _centroids[key] = centroids; // Replace entirely
            _writeHeat.TryRemove(key, out _); // Reset heat on centroid update? Or keep? Reset seems safer as IDs might change.
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

        public void RecordWrite(string tenantId, string indexName, float[] vector, VectorMetric metric)
        {
            // O(K) where K is number of clusters (small, e.g. 256)
            var (clusterId, _) = FindNearestCluster(tenantId, indexName, vector, metric);
            if (clusterId < 0) return;

            var indexKey = KeyUtils.GetIndexKey(tenantId, indexName);
            var clusterMap = _writeHeat.GetOrAdd(indexKey, _ => new ConcurrentDictionary<int, HeatState>());
            var state = clusterMap.GetOrAdd(clusterId, _ => new HeatState());

            var now = _timeProvider.GetUnixTimeSeconds();
            lock (state.Sync)
            {
                // Simple window reset (1 minute)
                if (now - state.WindowStart > 60)
                {
                    state.WindowStart = now;
                    state.Count = 0;
                }
                state.Count++;
            }
        }

        public TimeSpan GetRecommendedTTL(string tenantId, string indexName, int clusterId, TimeSpan baseTTL)
        {
            var indexKey = KeyUtils.GetIndexKey(tenantId, indexName);
            if (_writeHeat.TryGetValue(indexKey, out var clusterMap))
            {
                if (clusterMap.TryGetValue(clusterId, out var state))
                {
                    var now = _timeProvider.GetUnixTimeSeconds();
                    lock (state.Sync)
                    {
                        // Check if still hot (within last minute)
                        if (now - state.WindowStart <= 60)
                        {
                            // Threshold: 10 writes per minute -> Hot
                            if (state.Count > 10)
                            {
                                // Reduce TTL significantly (e.g., 10% of base or fixed 5s)
                                var reduced = TimeSpan.FromSeconds(baseTTL.TotalSeconds * 0.1);
                                if (reduced < TimeSpan.FromSeconds(1)) reduced = TimeSpan.FromSeconds(1);
                                return reduced;
                            }
                        }
                    }
                }
            }
            return baseTTL;
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

        private class HeatState
        {
            public long WindowStart { get; set; }
            public int Count { get; set; }
            public object Sync { get; } = new();
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
