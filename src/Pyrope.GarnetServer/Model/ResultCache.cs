using System;
using System.Text.Json;
using System.Text.Encodings.Web;
using Pyrope.GarnetServer.Services;
using Pyrope.GarnetServer.Vector;

namespace Pyrope.GarnetServer.Model
{
    public class ResultCache
    {
        private readonly ICacheStorage _storage;
        private readonly VectorIndexRegistry _indexRegistry;
        private readonly IMetricsCollector? _metrics;
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
             // For cleaner JSON, maybe optional
             PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
        };

        public ResultCache(ICacheStorage storage, VectorIndexRegistry indexRegistry, IMetricsCollector? metrics = null)
        {
            _storage = storage;
            _indexRegistry = indexRegistry;
            _metrics = metrics;
        }

        public bool TryGet(QueryKey key, out string? resultJson)
        {
            resultJson = null;

            // 1. Construct storage key
            var storageKey = GetStorageKey(key);

            // 2. Retrieve from storage
            if (!_storage.TryGet(storageKey, out var data) || data == null)
            {
                return false;
            }

            try 
            {
                // 3. Deserialize to DTO
                var cachedItemDto = JsonSerializer.Deserialize<CachedItemDto>(data, _jsonOptions);
                if (cachedItemDto?.Key == null) return false;

                // 4. Verify Exact Match (Collision Check)
                // Reconstruct QueryKey from DTO to use its Equals method
                var storedKey = cachedItemDto.Key.ToQueryKey();
                if (!storedKey.Equals(key))
                {
                    return false;
                }

                // 5. Verify Epoch (Invalidation Check)
                var currentEpoch = _indexRegistry.GetEpoch(key.TenantId, key.IndexName);
                if (cachedItemDto.Epoch != currentEpoch)
                {
                    _metrics?.RecordEviction("epoch_mismatch");
                    return false;
                }

                resultJson = cachedItemDto.ResultJson;
                return true;
            }
            catch (Exception) // Catch generic JSON/InvalidOperation exceptions
            {
                // corrupted data
                _metrics?.RecordEviction("corruption");
                return false;
            }
        }

        public void Set(QueryKey key, string resultJson, TimeSpan? ttl = null)
        {
            var currentEpoch = _indexRegistry.GetEpoch(key.TenantId, key.IndexName);
            var itemDto = new CachedItemDto
            {
                Key = CachedQueryKeyDto.FromQueryKey(key),
                ResultJson = resultJson,
                Epoch = currentEpoch
            };

            var data = JsonSerializer.SerializeToUtf8Bytes(itemDto, _jsonOptions);
            var storageKey = GetStorageKey(key);
            
            _storage.Set(storageKey, data, ttl);
        }

        private static string GetStorageKey(QueryKey key)
        {
            return $"cache:{key.TenantId}:{key.IndexName}:{key.GetHashCode()}";
        }

        private class CachedItemDto
        {
            public CachedQueryKeyDto Key { get; set; } = null!;
            public string ResultJson { get; set; } = "";
            public long Epoch { get; set; }
        }

        private class CachedQueryKeyDto
        {
            public string TenantId { get; set; } = "";
            public string IndexName { get; set; } = "";
            public float[] Vector { get; set; } = Array.Empty<float>();
            public int TopK { get; set; }
            public VectorMetric Metric { get; set; }
            public List<string> FilterTags { get; set; } = new();
            public long? SimHash { get; set; }

            public static CachedQueryKeyDto FromQueryKey(QueryKey key)
            {
                return new CachedQueryKeyDto
                {
                    TenantId = key.TenantId,
                    IndexName = key.IndexName,
                    Vector = key.Vector,
                    TopK = key.TopK,
                    Metric = key.Metric,
                    FilterTags = new List<string>(key.FilterTags),
                    SimHash = key.SimHash
                };
            }

            public QueryKey ToQueryKey()
            {
                return new QueryKey(TenantId, IndexName, Vector, TopK, Metric, FilterTags, SimHash);
            }
        }
    }
}
