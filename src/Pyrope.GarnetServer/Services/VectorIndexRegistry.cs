using System;
using System.Collections.Concurrent;
using System.Threading;
using Pyrope.GarnetServer.Vector;

namespace Pyrope.GarnetServer.Services
{
    public sealed class VectorIndexRegistry
    {
        private readonly ConcurrentDictionary<string, IndexState> _indices = new(StringComparer.Ordinal);

        public IVectorIndex GetOrCreate(string tenantId, string indexName, int dimension, VectorMetric metric)
        {
            if (string.IsNullOrWhiteSpace(tenantId)) throw new ArgumentException("Tenant id cannot be empty.", nameof(tenantId));
            if (string.IsNullOrWhiteSpace(indexName)) throw new ArgumentException("Index name cannot be empty.", nameof(indexName));

            var key = GetIndexKey(tenantId, indexName);
            var state = _indices.GetOrAdd(key, _ => new IndexState(dimension, metric));

            if (state.Dimension != dimension)
            {
                throw new ArgumentException("Vector dimension mismatch.", nameof(dimension));
            }

            if (state.Metric != metric)
            {
                throw new ArgumentException("Vector metric mismatch.", nameof(metric));
            }

            return state.Index;
        }

        public bool TryGetIndex(string tenantId, string indexName, out IVectorIndex index)
        {
            if (string.IsNullOrWhiteSpace(tenantId)) throw new ArgumentException("Tenant id cannot be empty.", nameof(tenantId));
            if (string.IsNullOrWhiteSpace(indexName)) throw new ArgumentException("Index name cannot be empty.", nameof(indexName));

            var key = GetIndexKey(tenantId, indexName);
            if (_indices.TryGetValue(key, out var state))
            {
                index = state.Index;
                return true;
            }

            index = null!;
            return false;
        }

        public long IncrementEpoch(string tenantId, string indexName)
        {
            if (string.IsNullOrWhiteSpace(tenantId)) throw new ArgumentException("Tenant id cannot be empty.", nameof(tenantId));
            if (string.IsNullOrWhiteSpace(indexName)) throw new ArgumentException("Index name cannot be empty.", nameof(indexName));

            var key = GetIndexKey(tenantId, indexName);
            return _indices.TryGetValue(key, out var state) ? state.IncrementEpoch() : 0;
        }

        public long GetEpoch(string tenantId, string indexName)
        {
            if (string.IsNullOrWhiteSpace(tenantId)) throw new ArgumentException("Tenant id cannot be empty.", nameof(tenantId));
            if (string.IsNullOrWhiteSpace(indexName)) throw new ArgumentException("Index name cannot be empty.", nameof(indexName));

            var key = GetIndexKey(tenantId, indexName);
            return _indices.TryGetValue(key, out var state) ? state.Epoch : 0;
        }

        public void Clear()
        {
            _indices.Clear();
        }

        private static string GetIndexKey(string tenantId, string indexName) => $"{tenantId}:{indexName}";

        private sealed class IndexState
        {
            private long _epoch;

            public IndexState(int dimension, VectorMetric metric)
            {
                Dimension = dimension;
                Metric = metric;
                Index = new BruteForceVectorIndex(dimension, metric);
            }

            public int Dimension { get; }
            public VectorMetric Metric { get; }
            public IVectorIndex Index { get; }
            public long Epoch => Interlocked.Read(ref _epoch);

            public long IncrementEpoch()
            {
                return Interlocked.Increment(ref _epoch);
            }
        }
    }
}
