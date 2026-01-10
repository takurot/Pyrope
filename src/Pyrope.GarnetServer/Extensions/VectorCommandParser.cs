using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Garnet.server;
using Pyrope.GarnetServer.Utils;

namespace Pyrope.GarnetServer.Extensions
{
    public sealed class VectorCommandRequest
    {
        public VectorCommandRequest(
            string tenantId,
            string indexName,
            string id,
            float[] vector,
            string? metaJson,
            IReadOnlyList<string> tags,
            IReadOnlyDictionary<string, double> numericFields,
            string? apiKey)
        {
            TenantId = tenantId;
            IndexName = indexName;
            Id = id;
            Vector = vector;
            MetaJson = metaJson;
            Tags = tags;
            NumericFields = numericFields;
            ApiKey = apiKey;
        }

        public string TenantId { get; }
        public string IndexName { get; }
        public string Id { get; }
        public float[] Vector { get; }
        public string? MetaJson { get; }
        public IReadOnlyList<string> Tags { get; }
        public IReadOnlyDictionary<string, double> NumericFields { get; }
        public string? ApiKey { get; }
    }

    public sealed class VectorSearchRequest
    {
        public VectorSearchRequest(
            string tenantId,
            string indexName,
            int topK,
            float[] vector,
            IReadOnlyList<string> filterTags,
            bool includeMeta,
            bool trace,
            string? requestId,
            string? apiKey,
            CacheHint cacheHint)
        {
            TenantId = tenantId;
            IndexName = indexName;
            TopK = topK;
            Vector = vector;
            FilterTags = filterTags;
            IncludeMeta = includeMeta;
            Trace = trace;
            RequestId = requestId;
            ApiKey = apiKey;
            CacheHint = cacheHint;
        }

        public string TenantId { get; }
        public string IndexName { get; }
        public int TopK { get; }
        public float[] Vector { get; }
        public IReadOnlyList<string> FilterTags { get; }
        public bool IncludeMeta { get; }
        public bool Trace { get; }
        public string? RequestId { get; }
        public string? ApiKey { get; }
        public CacheHint CacheHint { get; }
    }

    public enum CacheHint
    {
        Default = 0,
        Force = 1
    }

    public static class VectorCommandParser
    {
        public static VectorCommandRequest Parse(IReadOnlyList<string> args)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));
            if (args.Count < 5)
            {
                throw new ArgumentException("Expected at least 5 arguments: tenant index id VECTOR <payload>.");
            }

            var tenantId = args[0];
            TenantNamespace.ValidateTenantId(tenantId);
            var indexName = args[1];
            TenantNamespace.ValidateIndexName(indexName);
            var id = args[2];
            var vectorToken = args[3];
            if (!vectorToken.Equals("VECTOR", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Expected VECTOR token before payload.");
            }

            var vector = VectorParsing.ParseVector(Encoding.UTF8.GetBytes(args[4]));
            string? metaJson = null;
            var tags = Array.Empty<string>();
            var numericFields = new Dictionary<string, double>(StringComparer.Ordinal);
            string? apiKey = null;

            var i = 5;
            while (i < args.Count)
            {
                var token = args[i];
                if (token.Equals("META", StringComparison.OrdinalIgnoreCase))
                {
                    i++;
                    if (i >= args.Count)
                    {
                        throw new ArgumentException("META requires a JSON payload.");
                    }
                    metaJson = ValidateJson(args[i]);
                    i++;
                    continue;
                }

                if (token.Equals("TAGS", StringComparison.OrdinalIgnoreCase))
                {
                    i++;
                    if (i >= args.Count)
                    {
                        throw new ArgumentException("TAGS requires a JSON array or comma-separated list.");
                    }
                    tags = ParseTags(args[i]);
                    i++;
                    continue;
                }

                if (token.Equals("NUMERIC_FIELDS", StringComparison.OrdinalIgnoreCase))
                {
                    i++;
                    if (i >= args.Count)
                    {
                        throw new ArgumentException("NUMERIC_FIELDS requires a JSON object.");
                    }
                    numericFields = ParseNumericFields(args[i]);
                    i++;
                    continue;
                }

                if (token.Equals("API_KEY", StringComparison.OrdinalIgnoreCase))
                {
                    i++;
                    if (i >= args.Count)
                    {
                        throw new ArgumentException("API_KEY requires a value.");
                    }
                    apiKey = args[i];
                    i++;
                    continue;
                }

                throw new ArgumentException($"Unknown token '{token}'.");
            }

            return new VectorCommandRequest(tenantId, indexName, id, vector, metaJson, tags, numericFields, apiKey);
        }

        public static VectorCommandRequest Parse(string tenantId, IReadOnlyList<ArgSlice> args)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));
            TenantNamespace.ValidateTenantId(tenantId);
            if (args.Count < 4)
            {
                throw new ArgumentException("Expected at least 4 arguments: index id VECTOR <payload>.");
            }

            var indexName = Decode(args[0]);
            TenantNamespace.ValidateIndexName(indexName);
            var id = Decode(args[1]);
            var vectorToken = Decode(args[2]);
            if (!vectorToken.Equals("VECTOR", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Expected VECTOR token before payload.");
            }

            var vector = VectorParsing.ParseVector(args[3].ReadOnlySpan);
            string? metaJson = null;
            var tags = Array.Empty<string>();
            var numericFields = new Dictionary<string, double>(StringComparer.Ordinal);
            string? apiKey = null;

            var i = 4;
            while (i < args.Count)
            {
                var token = Decode(args[i]);
                if (token.Equals("META", StringComparison.OrdinalIgnoreCase))
                {
                    i++;
                    if (i >= args.Count)
                    {
                        throw new ArgumentException("META requires a JSON payload.");
                    }
                    metaJson = ValidateJson(Decode(args[i]));
                    i++;
                    continue;
                }

                if (token.Equals("TAGS", StringComparison.OrdinalIgnoreCase))
                {
                    i++;
                    if (i >= args.Count)
                    {
                        throw new ArgumentException("TAGS requires a JSON array or comma-separated list.");
                    }
                    tags = ParseTags(Decode(args[i]));
                    i++;
                    continue;
                }

                if (token.Equals("NUMERIC_FIELDS", StringComparison.OrdinalIgnoreCase))
                {
                    i++;
                    if (i >= args.Count)
                    {
                        throw new ArgumentException("NUMERIC_FIELDS requires a JSON object.");
                    }
                    numericFields = ParseNumericFields(Decode(args[i]));
                    i++;
                    continue;
                }

                if (token.Equals("API_KEY", StringComparison.OrdinalIgnoreCase))
                {
                    i++;
                    if (i >= args.Count)
                    {
                        throw new ArgumentException("API_KEY requires a value.");
                    }
                    apiKey = Decode(args[i]);
                    i++;
                    continue;
                }

                throw new ArgumentException($"Unknown token '{token}'.");
            }

            return new VectorCommandRequest(tenantId, indexName, id, vector, metaJson, tags, numericFields, apiKey);
        }

        public static VectorSearchRequest ParseSearch(string tenantId, IReadOnlyList<ArgSlice> args)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));
            TenantNamespace.ValidateTenantId(tenantId);
            if (args.Count < 5)
            {
                throw new ArgumentException("Expected at least 5 arguments: index TOPK <k> VECTOR <payload>.");
            }

            var indexName = Decode(args[0]);
            TenantNamespace.ValidateIndexName(indexName);
            var token = Decode(args[1]);
            if (!token.Equals("TOPK", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Expected TOPK token after index name.");
            }

            if (!int.TryParse(Decode(args[2]), out var topK) || topK <= 0)
            {
                throw new ArgumentException("TOPK must be a positive integer.");
            }

            token = Decode(args[3]);
            if (!token.Equals("VECTOR", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Expected VECTOR token before payload.");
            }

            var vector = VectorParsing.ParseVector(args[4].ReadOnlySpan);
            var filterTags = Array.Empty<string>();
            var includeMeta = false;
            var trace = false;
            string? requestId = null;
            string? apiKey = null;
            var cacheHint = CacheHint.Default;

            var i = 5;
            while (i < args.Count)
            {
                token = Decode(args[i]);
                if (token.Equals("FILTER", StringComparison.OrdinalIgnoreCase))
                {
                    i++;
                    if (i >= args.Count)
                    {
                        throw new ArgumentException("FILTER requires tag list.");
                    }
                    filterTags = ParseTags(Decode(args[i]));
                    i++;
                    continue;
                }

                if (token.Equals("WITH_META", StringComparison.OrdinalIgnoreCase))
                {
                    includeMeta = true;
                    i++;
                    continue;
                }

                if (token.Equals("TRACE", StringComparison.OrdinalIgnoreCase))
                {
                    trace = true;
                    i++;
                    continue;
                }

                if (token.Equals("REQUEST_ID", StringComparison.OrdinalIgnoreCase))
                {
                    i++;
                    if (i >= args.Count)
                    {
                        throw new ArgumentException("REQUEST_ID requires a value.");
                    }
                    requestId = Decode(args[i]);
                    i++;
                    continue;
                }

                if (token.Equals("CACHE_HINT", StringComparison.OrdinalIgnoreCase))
                {
                    i++;
                    if (i >= args.Count)
                    {
                        throw new ArgumentException("CACHE_HINT requires a value.");
                    }
                    var value = Decode(args[i]);
                    if (value.Equals("force", StringComparison.OrdinalIgnoreCase))
                    {
                        cacheHint = CacheHint.Force;
                    }
                    else
                    {
                        throw new ArgumentException("CACHE_HINT must be 'force'.");
                    }
                    i++;
                    continue;
                }

                if (token.Equals("API_KEY", StringComparison.OrdinalIgnoreCase))
                {
                    i++;
                    if (i >= args.Count)
                    {
                        throw new ArgumentException("API_KEY requires a value.");
                    }
                    apiKey = Decode(args[i]);
                    i++;
                    continue;
                }

                throw new ArgumentException($"Unknown token '{token}'.");
            }

            return new VectorSearchRequest(tenantId, indexName, topK, vector, filterTags, includeMeta, trace, requestId, apiKey, cacheHint);
        }

        private static string ValidateJson(string json)
        {
            using var _ = JsonDocument.Parse(json);
            return json;
        }

        private static string[] ParseTags(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Array.Empty<string>();
            }

            if (value.TrimStart().StartsWith("[", StringComparison.Ordinal))
            {
                var parsed = JsonSerializer.Deserialize<string[]>(value);
                return parsed ?? Array.Empty<string>();
            }

            return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        private static Dictionary<string, double> ParseNumericFields(string value)
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, double>>(value);
            return parsed ?? new Dictionary<string, double>(StringComparer.Ordinal);
        }

        private static string Decode(ArgSlice arg)
        {
            return Encoding.UTF8.GetString(arg.ReadOnlySpan);
        }
    }
}
