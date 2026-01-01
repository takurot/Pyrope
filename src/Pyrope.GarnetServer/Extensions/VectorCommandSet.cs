using System;
using System.Collections.Generic;
using Garnet.common;
using Garnet.server;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Services;
using Pyrope.GarnetServer.Vector;
using Pyrope.GarnetServer.Policies;
using Tsavorite.core;

namespace Pyrope.GarnetServer.Extensions
{
    public class VectorCommandSet : CustomRawStringFunctions
    {
        // Command IDs check
        public const int VEC_ADD = 10;
        public const int VEC_UPSERT = 11;
        public const int VEC_DEL = 12;
        public const int VEC_SEARCH = 13;
        public const int VEC_STATS = 14;
        
        public static VectorIndexRegistry SharedIndexRegistry => IndexRegistry;
        
        private static readonly VectorStore Store = new();
        private static readonly VectorIndexRegistry IndexRegistry = new();
        
        private readonly VectorCommandType _commandType;
        private readonly ResultCache? _resultCache;
        private readonly IPolicyEngine? _policyEngine;
        private readonly IMetricsCollector? _metrics;
        private readonly LshService? _lshService;

        public VectorCommandSet(VectorCommandType commandType, ResultCache? resultCache = null, IPolicyEngine? policyEngine = null, IMetricsCollector? metrics = null, LshService? lshService = null)
        {
            _commandType = commandType;
            _resultCache = resultCache;
            _policyEngine = policyEngine;
            _metrics = metrics;
            _lshService = lshService;
        }

        public override bool InitialUpdater(ReadOnlySpan<byte> key, ref RawStringInput input, Span<byte> value, ref RespMemoryWriter output, ref RMWInfo rmwInfo)
            => HandleWrite(key, ref input, ref output);

        public override int GetInitialLength(ref RawStringInput input) => 0;

        public override int GetLength(ReadOnlySpan<byte> value, ref RawStringInput input) => value.Length;

        public override bool InPlaceUpdater(ReadOnlySpan<byte> key, ref RawStringInput input, Span<byte> value, ref int valueLength, ref RespMemoryWriter output, ref RMWInfo rmwInfo)
            => HandleWrite(key, ref input, ref output);

        public override bool CopyUpdater(ReadOnlySpan<byte> key, ref RawStringInput input, ReadOnlySpan<byte> oldValue, Span<byte> newValue, ref RespMemoryWriter output, ref RMWInfo rmwInfo)
            => HandleWrite(key, ref input, ref output);

        public override bool Reader(ReadOnlySpan<byte> key, ref RawStringInput input, ReadOnlySpan<byte> value, ref RespMemoryWriter output, ref ReadInfo readInfo)
        {
            if (_commandType != VectorCommandType.Search && _commandType != VectorCommandType.Stats)
            {
                WriteErrorCode(ref output, "ERR Unsupported read command.");
                return true;
            }

            // --- STATS COMMAND ---
            if (_commandType == VectorCommandType.Stats)
            {
                if (_metrics == null)
                {
                    output.WriteError("ERR Metrics collector not configured.");
                    return true;
                }
                var stats = _metrics.GetStats();
                output.WriteUtf8BulkString(stats);
                return true;
            }

            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var tenantId = System.Text.Encoding.UTF8.GetString(key);
                var args = ReadArgs(ref input);
                var request = VectorCommandParser.ParseSearch(tenantId, args);


                // --- CACHE LOOKUP ---
                QueryKey? queryKey = null;
                PolicyDecision policyDecision = PolicyDecision.NoCache;

                if (_policyEngine != null && _resultCache != null)
                {
                    queryKey = new QueryKey(request.TenantId, request.IndexName, request.Vector, request.TopK, VectorMetric.L2, request.FilterTags);
                    policyDecision = _policyEngine.Evaluate(queryKey);
                    
                    if (policyDecision.ShouldCache)
                    {
                        // 1. Try L0 (Precise/Exact)
                         if (_resultCache.TryGet(queryKey, out var cachedJson) && !string.IsNullOrEmpty(cachedJson))
                         {
                             var cachedHits = System.Text.Json.JsonSerializer.Deserialize<List<SearchHitDto>>(cachedJson);
                             if (cachedHits != null)
                             {
                                 WriteResults(ref output, cachedHits, request.IncludeMeta);
                                 _metrics?.RecordCacheHit();
                                 sw.Stop();
                                 _metrics?.RecordSearchLatency(sw.Elapsed);
                                 return true;
                             }
                         }
                         else
                         {
                             // 2. Try L1 (Semantic/Fuzzy)
                             if (_lshService != null)
                             {
                                 var simHash = _lshService.GenerateSimHash(request.Vector);
                                 var roundedK = QueryKey.RoundK(request.TopK);
                                 var l1Key = new QueryKey(request.TenantId, request.IndexName, request.Vector, roundedK, VectorMetric.L2, request.FilterTags, simHash);
                                 
                                 if (_resultCache.TryGet(l1Key, out var l1Json) && !string.IsNullOrEmpty(l1Json))
                                 {
                                     var l1Hits = System.Text.Json.JsonSerializer.Deserialize<List<SearchHitDto>>(l1Json);
                                     if (l1Hits != null)
                                     {
                                         WriteResults(ref output, l1Hits, request.IncludeMeta);
                                         _metrics?.RecordCacheHit(); // Count as hit (maybe distinguish later)
                                         sw.Stop();
                                         _metrics?.RecordSearchLatency(sw.Elapsed);
                                         return true;
                                     }
                                 }
                                 // Close L1 check
                             }

                             // If we reached here, both L0 and L1 (if enabled) missed
                             _metrics?.RecordCacheMiss();
                         }
                    }
                }

                if (!IndexRegistry.TryGetIndex(request.TenantId, request.IndexName, out var index))
                {
                    WriteErrorCode(ref output, VectorErrorCodes.NotFound, "Index not found.");
                    return true;
                }

                if (index.Dimension != request.Vector.Length)
                {
                    WriteErrorCode(ref output, VectorErrorCodes.DimMismatch, "Vector dimension mismatch.");
                    return true;
                }

                var rawResults = index.Search(request.Vector, request.TopK);
                var results = new List<SearchHitDto>(rawResults.Count);
                foreach (var hit in rawResults)
                {
                    if (!Store.TryGet(request.TenantId, request.IndexName, hit.Id, out var record))
                    {
                        continue;
                    }

                    if (record.Deleted)
                    {
                        continue;
                    }

                    if (request.FilterTags.Count > 0 && !HasAllTags(record.Tags, request.FilterTags))
                    {
                        continue;
                    }

                    results.Add(new SearchHitDto { Id = hit.Id, Score = hit.Score, MetaJson = record.MetaJson });
                }

                WriteResults(ref output, results, request.IncludeMeta);

                // --- CACHE SET ---
                if (policyDecision.ShouldCache && queryKey != null && _resultCache != null)
                {
                    var json = System.Text.Json.JsonSerializer.Serialize(results);
                    
                    // Set L0 (Exact)
                    _resultCache.Set(queryKey, json, policyDecision.Ttl);

                    // Set L1 (Semantic)
                    if (_lshService != null)
                    {
                        var simHash = _lshService.GenerateSimHash(request.Vector);
                        var roundedK = QueryKey.RoundK(request.TopK);
                        var l1Key = new QueryKey(request.TenantId, request.IndexName, request.Vector, roundedK, VectorMetric.L2, request.FilterTags, simHash);
                        _resultCache.Set(l1Key, json, policyDecision.Ttl);
                    }
                }

                sw.Stop();
                _metrics?.RecordSearchLatency(sw.Elapsed);
                return true;
            }
            catch (Exception ex)
            {
                if (!TryWriteKnownError(ex, ref output))
                {
                    WriteErrorCode(ref output, $"ERR {ex.Message}");
                }
                return true;
            }
        }

        private bool HandleWrite(ReadOnlySpan<byte> key, ref RawStringInput input, ref RespMemoryWriter output)
        {
            if (_commandType == VectorCommandType.Search)
            {
                WriteErrorCode(ref output, "ERR VEC.SEARCH is read-only.");
                return true;
            }

            try
            {
                if (_commandType == VectorCommandType.Del)
                {
                    return HandleDelete(key, ref input, ref output);
                }

                var tenantId = System.Text.Encoding.UTF8.GetString(key);
                var args = ReadArgs(ref input);
                var request = VectorCommandParser.Parse(tenantId, args);

                var record = new VectorRecord(
                    request.TenantId,
                    request.IndexName,
                    request.Id,
                    request.Vector,
                    request.MetaJson,
                    request.Tags,
                    request.NumericFields,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow);

                var index = IndexRegistry.GetOrCreate(request.TenantId, request.IndexName, request.Vector.Length, VectorMetric.L2);

                if (_commandType == VectorCommandType.Add)
                {
                    if (!Store.TryAdd(record))
                    {
                        WriteErrorCode(ref output, "ERR Vector already exists.");
                        return true;
                    }

                    index.Add(request.Id, request.Vector);
                }
                else if (_commandType == VectorCommandType.Upsert)
                {
                    Store.Upsert(record);
                    index.Upsert(request.Id, request.Vector);
                }
                else
                {
                    WriteErrorCode(ref output, "ERR Unsupported write command.");
                    return true;
                }

                IndexRegistry.IncrementEpoch(request.TenantId, request.IndexName);
                output.WriteSimpleString(VectorErrorCodes.Ok);
                return true;
            }
            catch (Exception ex)
            {
                if (!TryWriteKnownError(ex, ref output))
                {
                    WriteErrorCode(ref output, $"ERR {ex.Message}");
                }
                return true;
            }
        }

        private bool HandleDelete(ReadOnlySpan<byte> key, ref RawStringInput input, ref RespMemoryWriter output)
        {
            var tenantId = System.Text.Encoding.UTF8.GetString(key);
            var args = ReadArgs(ref input);
            if (args.Count < 2)
            {
                WriteErrorCode(ref output, "ERR Expected 2 arguments: index id.");
                return true;
            }

            if (args.Count > 2)
            {
                WriteErrorCode(ref output, "ERR Too many arguments for VEC.DEL.");
                return true;
            }

            var indexName = Decode(args[0]);
            var id = Decode(args[1]);

            if (IndexRegistry.TryGetIndex(tenantId, indexName, out var index))
            {
                var deleted = Store.TryMarkDeleted(tenantId, indexName, id);
                index.Delete(id);

                if (deleted)
                {
                    IndexRegistry.IncrementEpoch(tenantId, indexName);
                }
            }
            else
            {
                WriteErrorCode(ref output, VectorErrorCodes.NotFound, "Index not found.");
                return true;
            }

            output.WriteSimpleString(VectorErrorCodes.Ok);
            return true;
        }

        private static List<ArgSlice> ReadArgs(ref RawStringInput input)
        {
            var args = new List<ArgSlice>();
            var count = input.parseState.Count;
            for (var i = 0; i < count; i++)
            {
                args.Add(input.parseState.GetArgSliceByRef(i));
            }
            return args;
        }

        private static string Decode(ArgSlice arg)
        {
            return System.Text.Encoding.UTF8.GetString(arg.ReadOnlySpan);
        }

        private static bool HasAllTags(IReadOnlyList<string> recordTags, IReadOnlyList<string> filterTags)
        {
            if (filterTags.Count == 0)
            {
                return true;
            }

            if (recordTags.Count == 0)
            {
                return false;
            }

            var set = new HashSet<string>(recordTags, StringComparer.Ordinal);
            foreach (var tag in filterTags)
            {
                if (!set.Contains(tag))
                {
                    return false;
                }
            }

            return true;
        }

        private static void WriteErrorCode(ref RespMemoryWriter output, string code, string? detail = null)
        {
            if (string.IsNullOrWhiteSpace(detail))
            {
                output.WriteError(code);
                return;
            }

            output.WriteError($"{code} {detail}");
        }

        private static bool TryWriteKnownError(Exception ex, ref RespMemoryWriter output)
        {
            if (ex is ArgumentException argEx &&
                argEx.Message.Contains("dimension", StringComparison.OrdinalIgnoreCase))
            {
                WriteErrorCode(ref output, VectorErrorCodes.DimMismatch, "Vector dimension mismatch.");
                return true;
            }

            return false;
        }

        private static void WriteResults(ref RespMemoryWriter output, List<SearchHitDto> results, bool includeMeta)
        {
            output.WriteArrayLength(results.Count);
            foreach (var hit in results)
            {
                output.WriteArrayLength(includeMeta ? 3 : 2);
                output.WriteUtf8BulkString(hit.Id);
                output.WriteDoubleNumeric(hit.Score);
                if (includeMeta)
                {
                    if (hit.MetaJson is null)
                    {
                        output.WriteNull();
                    }
                    else
                    {
                        output.WriteUtf8BulkString(hit.MetaJson);
                    }
                }
            }
        }

        public class SearchHitDto
        {
            public string Id { get; set; } = "";
            public float Score { get; set; }
            public string? MetaJson { get; set; }
        }
    }

    public enum VectorCommandType
    {
        Add,
        Upsert,
        Del,
        Search,
        Stats
    }
}
