using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using Garnet.common;
using Garnet.server;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Security;
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
        private readonly ITenantQuotaEnforcer? _quotaEnforcer;
        private readonly ITenantAuthenticator? _tenantAuthenticator;
        private readonly ISloGuardrails? _sloGuardrails;

        public VectorCommandSet(
            VectorCommandType commandType,
            ResultCache? resultCache = null,
            IPolicyEngine? policyEngine = null,
            IMetricsCollector? metrics = null,
            LshService? lshService = null,
            ITenantQuotaEnforcer? quotaEnforcer = null,
            ITenantAuthenticator? tenantAuthenticator = null,
            ISloGuardrails? sloGuardrails = null)
        {
            _commandType = commandType;
            _resultCache = resultCache;
            _policyEngine = policyEngine;
            _metrics = metrics;
            _lshService = lshService;
            _quotaEnforcer = quotaEnforcer;
            _tenantAuthenticator = tenantAuthenticator;
            _sloGuardrails = sloGuardrails;
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

            var tenantId = System.Text.Encoding.UTF8.GetString(key);

            // --- STATS COMMAND ---
            if (_commandType == VectorCommandType.Stats)
            {
                if (_metrics == null)
                {
                    output.WriteError("ERR Metrics collector not configured.");
                    return true;
                }
                try
                {
                    var args = ReadArgs(ref input);
                    if (!TryParseApiKeyOnly(args, out var apiKey, out var parseError))
                    {
                        WriteErrorCode(ref output, VectorErrorCodes.Auth, parseError);
                        return true;
                    }

                    if (!TryAuthenticate(tenantId, apiKey, ref output))
                    {
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    WriteErrorCode(ref output, $"ERR {ex.Message}");
                    return true;
                }
                var stats = _metrics.GetStats();
                output.WriteUtf8BulkString(stats);
                return true;
            }

            try
            {
                var totalStart = Stopwatch.GetTimestamp();
                TenantRequestLease? lease = null;
                if (_quotaEnforcer != null && !_quotaEnforcer.TryBeginRequest(tenantId, out lease, out var errorCode, out var errorMessage))
                {
                    WriteErrorCode(ref output, errorCode ?? VectorErrorCodes.Busy, errorMessage ?? "Tenant quota exceeded.");
                    return true;
                }

                try
                {
                    var args = ReadArgs(ref input);
                    var request = VectorCommandParser.ParseSearch(tenantId, args);
                    if (!TryAuthenticate(tenantId, request.ApiKey, ref output))
                    {
                        return true;
                    }
                    var traceEnabled = request.Trace;
                    var requestId = request.RequestId;
                    if (traceEnabled && string.IsNullOrWhiteSpace(requestId))
                    {
                        requestId = Guid.NewGuid().ToString("N");
                    }


                    // --- CACHE LOOKUP ---
                    QueryKey? queryKey = null;
                    PolicyDecision policyDecision = PolicyDecision.NoCache;
                    long policyStart = 0;
                    long policyEnd = 0;
                    long cacheStart = 0;
                    long cacheEnd = 0;
                    long faissStart = 0;
                    long faissEnd = 0;
                    var cacheHit = false;

                    if (_policyEngine != null && _resultCache != null)
                    {
                        policyStart = Stopwatch.GetTimestamp();
                        queryKey = new QueryKey(request.TenantId, request.IndexName, request.Vector, request.TopK, VectorMetric.L2, request.FilterTags);
                        policyDecision = _policyEngine.Evaluate(queryKey);
                        policyEnd = Stopwatch.GetTimestamp();

                        if (policyDecision.ShouldCache)
                        {
                            cacheStart = Stopwatch.GetTimestamp();
                            // 1. Try L0 (Precise/Exact)
                            if (_resultCache.TryGet(queryKey, out var cachedJson) && !string.IsNullOrEmpty(cachedJson))
                            {
                                var cachedHits = System.Text.Json.JsonSerializer.Deserialize<List<SearchHitDto>>(cachedJson);
                                if (cachedHits != null)
                                {
                                    cacheHit = true;
                                    cacheEnd = Stopwatch.GetTimestamp();
                                    var totalEnd = Stopwatch.GetTimestamp();
                                    var traceJson = traceEnabled
                                        ? JsonSerializer.Serialize(new TraceInfo
                                        {
                                            RequestId = requestId,
                                            CacheHit = true,
                                            LatencyMs = ElapsedMilliseconds(totalStart, totalEnd),
                                            PolicyMs = ElapsedMilliseconds(policyStart, policyEnd),
                                            CacheMs = ElapsedMilliseconds(cacheStart, cacheEnd),
                                            FaissMs = 0
                                        })
                                        : null;
                                    WriteResults(ref output, cachedHits, request.IncludeMeta, traceJson);
                                    _metrics?.RecordCacheHit();
                                    _metrics?.RecordSearchLatency(TimeSpan.FromMilliseconds(ElapsedMilliseconds(totalStart, totalEnd)));
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
                                            cacheHit = true;
                                            cacheEnd = Stopwatch.GetTimestamp();
                                            var totalEnd = Stopwatch.GetTimestamp();
                                            var traceJson = traceEnabled
                                                ? JsonSerializer.Serialize(new TraceInfo
                                                {
                                                    RequestId = requestId,
                                                    CacheHit = true,
                                                    LatencyMs = ElapsedMilliseconds(totalStart, totalEnd),
                                                    PolicyMs = ElapsedMilliseconds(policyStart, policyEnd),
                                                    CacheMs = ElapsedMilliseconds(cacheStart, cacheEnd),
                                                    FaissMs = 0
                                                })
                                                : null;
                                            WriteResults(ref output, l1Hits, request.IncludeMeta, traceJson);
                                            _metrics?.RecordCacheHit(); // Count as hit (maybe distinguish later)
                                            _metrics?.RecordSearchLatency(TimeSpan.FromMilliseconds(ElapsedMilliseconds(totalStart, totalEnd)));
                                            return true;
                                        }
                                    }
                                    // Close L1 check
                                }

                                // If we reached here, both L0 and L1 (if enabled) missed
                                cacheEnd = Stopwatch.GetTimestamp();
                                _metrics?.RecordCacheMiss();
                            }
                        }
                    }

                    // --- SLO Guardrails: cache-only shedding ---
                    // - Explicit: CACHE_HINT=force
                    // - Automatic: low priority tenants when guardrails are degraded
                    var forceCacheOnly =
                        request.CacheHint == CacheHint.Force ||
                        (_sloGuardrails?.ShouldForceCacheOnly(request.TenantId, request.IndexName) ?? false);
                    if (forceCacheOnly && !cacheHit)
                    {
                        WriteErrorCode(ref output, VectorErrorCodes.Busy, "SLO mode: cache-only.");
                        return true;
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

                    faissStart = Stopwatch.GetTimestamp();
                    var searchOptions = _sloGuardrails?.GetSearchOptions(request.TenantId, request.IndexName);
                    var rawResults = index.Search(request.Vector, request.TopK, searchOptions);
                    faissEnd = Stopwatch.GetTimestamp();
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

                    var totalFinish = Stopwatch.GetTimestamp();
                    var tracePayload = traceEnabled
                        ? JsonSerializer.Serialize(new TraceInfo
                        {
                            RequestId = requestId,
                            CacheHit = cacheHit,
                            LatencyMs = ElapsedMilliseconds(totalStart, totalFinish),
                            PolicyMs = ElapsedMilliseconds(policyStart, policyEnd),
                            CacheMs = ElapsedMilliseconds(cacheStart, cacheEnd),
                            FaissMs = ElapsedMilliseconds(faissStart, faissEnd)
                        })
                        : null;
                    WriteResults(ref output, results, request.IncludeMeta, tracePayload);

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

                    _metrics?.RecordSearchLatency(TimeSpan.FromMilliseconds(ElapsedMilliseconds(totalStart, totalFinish)));
                    return true;
                }
                finally
                {
                    lease?.Dispose();
                }
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
                var tenantId = System.Text.Encoding.UTF8.GetString(key);
                TenantRequestLease? lease = null;
                if (_quotaEnforcer != null && !_quotaEnforcer.TryBeginRequest(tenantId, out lease, out var errorCode, out var errorMessage))
                {
                    WriteErrorCode(ref output, errorCode ?? VectorErrorCodes.Busy, errorMessage ?? "Tenant quota exceeded.");
                    return true;
                }

                try
                {
                    if (_commandType == VectorCommandType.Del)
                    {
                        return HandleDelete(tenantId, ref input, ref output);
                    }

                    var args = ReadArgs(ref input);
                    var request = VectorCommandParser.Parse(tenantId, args);
                    if (!TryAuthenticate(tenantId, request.ApiKey, ref output))
                    {
                        return true;
                    }

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
                finally
                {
                    lease?.Dispose();
                }
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

        private bool HandleDelete(string tenantId, ref RawStringInput input, ref RespMemoryWriter output)
        {
            var args = ReadArgs(ref input);
            if (args.Count < 2)
            {
                WriteErrorCode(ref output, "ERR Expected 2 arguments: index id.");
                return true;
            }

            var indexName = Decode(args[0]);
            var id = Decode(args[1]);
            string? apiKey = null;

            var i = 2;
            while (i < args.Count)
            {
                var token = Decode(args[i]);
                if (token.Equals("API_KEY", StringComparison.OrdinalIgnoreCase))
                {
                    i++;
                    if (i >= args.Count)
                    {
                        WriteErrorCode(ref output, VectorErrorCodes.Auth, "API_KEY requires a value.");
                        return true;
                    }

                    apiKey = Decode(args[i]);
                    i++;
                    continue;
                }

                WriteErrorCode(ref output, $"ERR Unknown token '{token}'.");
                return true;
            }

            if (!TryAuthenticate(tenantId, apiKey, ref output))
            {
                return true;
            }

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

        private bool TryAuthenticate(string tenantId, string? apiKey, ref RespMemoryWriter output)
        {
            if (_tenantAuthenticator == null)
            {
                WriteErrorCode(ref output, VectorErrorCodes.Auth, "Authenticator not configured.");
                return false;
            }

            if (!_tenantAuthenticator.TryAuthenticate(tenantId, apiKey, out var errorMessage))
            {
                WriteErrorCode(ref output, VectorErrorCodes.Auth, errorMessage ?? "Unauthorized.");
                return false;
            }

            return true;
        }

        private static bool TryParseApiKeyOnly(IReadOnlyList<ArgSlice> args, out string? apiKey, out string? error)
        {
            apiKey = null;
            error = null;

            if (args.Count == 0)
            {
                error = "API_KEY is required.";
                return false;
            }

            // Expect: API_KEY <value>
            if (args.Count != 2)
            {
                error = "Expected: API_KEY <value>";
                return false;
            }

            var token = Decode(args[0]);
            if (!token.Equals("API_KEY", StringComparison.OrdinalIgnoreCase))
            {
                error = "Expected API_KEY token.";
                return false;
            }

            apiKey = Decode(args[1]);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                error = "API key cannot be empty.";
                return false;
            }

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

        private static void WriteResults(ref RespMemoryWriter output, List<SearchHitDto> results, bool includeMeta, string? traceJson)
        {
            if (traceJson is null)
            {
                WriteHits(ref output, results, includeMeta);
                return;
            }

            output.WriteArrayLength(2);
            WriteHits(ref output, results, includeMeta);
            output.WriteUtf8BulkString(traceJson);
        }

        private static void WriteHits(ref RespMemoryWriter output, List<SearchHitDto> results, bool includeMeta)
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

        private static double ElapsedMilliseconds(long start, long end)
        {
            if (start == 0 || end == 0 || end <= start)
            {
                return 0;
            }

            return (end - start) * 1000d / Stopwatch.Frequency;
        }

        public class SearchHitDto
        {
            public string Id { get; set; } = "";
            public float Score { get; set; }
            public string? MetaJson { get; set; }
        }

        private class TraceInfo
        {
            public string? RequestId { get; set; }
            public bool CacheHit { get; set; }
            public double LatencyMs { get; set; }
            public double PolicyMs { get; set; }
            public double CacheMs { get; set; }
            public double FaissMs { get; set; }
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
