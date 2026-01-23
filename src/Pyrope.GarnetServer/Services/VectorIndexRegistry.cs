using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Text.Json;
using Pyrope.GarnetServer.Vector;
using Pyrope.GarnetServer.Utils;
using Pyrope.GarnetServer.Model;

namespace Pyrope.GarnetServer.Services
{
    public sealed class VectorIndexRegistry
    {
        private readonly ConcurrentDictionary<string, IndexState> _indices = new(StringComparer.Ordinal);

        public IVectorIndex GetOrCreate(string tenantId, string indexName, int dimension, VectorMetric metric, IndexConfig? config = null)
        {
            TenantNamespace.ValidateTenantId(tenantId);
            TenantNamespace.ValidateIndexName(indexName);

            var key = GetIndexKey(tenantId, indexName);
            var state = _indices.GetOrAdd(key, _ => new IndexState(dimension, metric, config));

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

            public IndexState(int dimension, VectorMetric metric, IndexConfig? config)
            {
                Dimension = dimension;
                Metric = metric;

                IVectorIndex tail;
                string algo = config?.Algorithm?.ToUpperInvariant() ?? "IVF_FLAT"; // Default to IVF_FLAT if not specified to maintain backward compat for now

                if (algo == "HNSW")
                {
                    int m = GetIntParam(config, "m", 16);
                    int efConstruction = GetIntParam(config, "ef_construction", 200);
                    int efSearch = GetIntParam(config, "ef_search", 10);
                    tail = new HnswVectorIndex(dimension, metric, m, efConstruction, efSearch);
                }
                else
                {
                    // IVF_FLAT
                    int nList = GetIntParam(config, "nlist", 100);
                    tail = new IvfFlatVectorIndex(dimension, metric, nList);
                }

                // Use DeltaVectorIndex by default to exercise the new path
                var head = new BruteForceVectorIndex(dimension, metric);
                Index = new DeltaVectorIndex(head, tail);
            }

            private static int GetIntParam(IndexConfig? config, string key, int defaultValue)
            {
                if (config != null && config.Parameters != null && config.Parameters.TryGetValue(key, out var obj))
                {
                    if (obj is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Number)
                        return je.GetInt32();
                    if (obj is int val) return val;
                    if (obj is long lVal) return (int)lVal;
                    if (obj is string sVal && int.TryParse(sVal, out int parsed)) return parsed;
                }
                return defaultValue;
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
