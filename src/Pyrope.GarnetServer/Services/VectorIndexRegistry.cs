using System;
using System.Collections.Concurrent;
using System.Threading;
using Pyrope.GarnetServer.Vector;
using Pyrope.GarnetServer.Utils;

namespace Pyrope.GarnetServer.Services
{
    public sealed class VectorIndexRegistry
    {
        private readonly ConcurrentDictionary<string, IndexState> _indices = new(StringComparer.Ordinal);

        public IVectorIndex GetOrCreate(string tenantId, string indexName, int dimension, VectorMetric metric)
        {
            TenantNamespace.ValidateTenantId(tenantId);
            TenantNamespace.ValidateIndexName(indexName);

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
            TenantNamespace.ValidateTenantId(tenantId);
            TenantNamespace.ValidateIndexName(indexName);

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
            TenantNamespace.ValidateTenantId(tenantId);
            TenantNamespace.ValidateIndexName(indexName);

            var key = GetIndexKey(tenantId, indexName);
            return _indices.TryGetValue(key, out var state) ? state.IncrementEpoch() : 0;
        }

        public long GetEpoch(string tenantId, string indexName)
        {
            TenantNamespace.ValidateTenantId(tenantId);
            TenantNamespace.ValidateIndexName(indexName);

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
                // Use DeltaVectorIndex by default to exercise the new path
                var head = new BruteForceVectorIndex(dimension, metric);
                var tail = new BruteForceVectorIndex(dimension, metric);
                Index = new DeltaVectorIndex(head, tail);
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
