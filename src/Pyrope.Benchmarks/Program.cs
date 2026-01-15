using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Pyrope.Benchmarks.Datasets;
using Pyrope.Benchmarks.Encoding;
using Pyrope.Benchmarks.Stats;
using StackExchange.Redis;
using TextEncoding = System.Text.Encoding;

namespace Pyrope.Benchmarks;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help", StringComparer.OrdinalIgnoreCase) || args.Contains("-h"))
        {
            PrintUsage();
            return 0;
        }

        if (!TryParseArgs(args, out var options, out var error))
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine();
            PrintUsage();
            return 2;
        }

        if (string.IsNullOrWhiteSpace(options.TenantApiKey))
        {
            Console.Error.WriteLine("--api-key is required (tenant API key).");
            return 2;
        }

        if (!string.IsNullOrWhiteSpace(options.HttpBaseUrl) && string.IsNullOrWhiteSpace(options.AdminApiKey))
        {
            Console.Error.WriteLine("--admin-api-key is required when using --http.");
            return 2;
        }

        if (!string.IsNullOrWhiteSpace(options.HttpBaseUrl))
        {
            await EnsureTenantAsync(options.HttpBaseUrl!, options.AdminApiKey!, options.TenantId, options.TenantApiKey!);
        }

        if (!string.IsNullOrWhiteSpace(options.HttpBaseUrl) &&
            options.CacheMode.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            await DisableCacheAsync(options.HttpBaseUrl!, options.AdminApiKey!, flush: true);
        }

        Console.WriteLine($"[Pyrope.Benchmarks] RESP endpoint: {options.Host}:{options.Port}");
        Console.WriteLine($"[Pyrope.Benchmarks] tenant={options.TenantId} index={options.IndexName}");
        Console.WriteLine($"[Pyrope.Benchmarks] dataset={options.Dataset} payload={(options.UseBinaryPayload ? "binary(float32)" : "json")}");

        // Use simple connection string like the tests do
        var connectionString = $"{options.Host}:{options.Port},abortConnect=false";
        using var redis = ConnectionMultiplexer.Connect(connectionString);
        var db = redis.GetDatabase();

        var (baseVectors, queryVectors, dimension) = LoadDataset(options);
        if (queryVectors.Count == 0)
        {
            Console.Error.WriteLine("No query vectors loaded; check dataset paths / limits.");
            return 2;
        }

        // Pre-encode queries to avoid per-request allocations in the hot loop
        var rawQueries = queryVectors;
        if (options.UniqueQueries > 0 && options.UniqueQueries < rawQueries.Count)
        {
            rawQueries = rawQueries.Take(options.UniqueQueries).ToList();
        }

        var uniqueEncoded = rawQueries
            .Select(v => options.UseBinaryPayload ? (RedisValue)VectorEncoding.ToLittleEndianBytes(v) : (RedisValue)JsonSerializer.Serialize(v))
            .ToArray();

        RedisValue[] encodedQueries;
        if (options.Repeat > 1)
        {
            var repeated = new List<RedisValue>(options.QueryLimit);
            for (var i = 0; i < options.QueryLimit; i++)
            {
                repeated.Add(uniqueEncoded[i % uniqueEncoded.Length]);
            }
            encodedQueries = repeated.ToArray();
        }
        else if (options.Sequence)
        {
            // Simple sequence for prefetch testing: A, B, A, B, ...
            // Expects at least 2 unique queries.
            var seq = new List<RedisValue>(options.QueryLimit);
            for (var i = 0; i < options.QueryLimit; i++)
            {
                seq.Add(uniqueEncoded[i % Math.Min(uniqueEncoded.Length, 2)]);
            }
            encodedQueries = seq.ToArray();
        }
        else
        {
            encodedQueries = uniqueEncoded;
        }

        // 1) Load base vectors
        Console.WriteLine($"[Pyrope.Benchmarks] Loading base vectors: {options.BaseLimit} ...");
        var load = await LoadVectorsAsync(
            db,
            options.TenantId,
            options.TenantApiKey!,
            options.IndexName,
            baseVectors,
            options.Concurrency,
            options.IdPrefix,
            options.UseBinaryPayload);

        Console.WriteLine($"[Pyrope.Benchmarks] Loaded={load.Loaded} elapsed={load.Elapsed.TotalSeconds:F2}s throughput={load.Throughput:0.0} vec/s");
        Console.WriteLine($"[Pyrope.Benchmarks] Loaded={load.Loaded} elapsed={load.Elapsed.TotalSeconds:F2}s throughput={load.Throughput:0.0} vec/s");
        Console.WriteLine();

        if (options.BuildIndex && !string.IsNullOrWhiteSpace(options.HttpBaseUrl))
        {
            Console.WriteLine("[Pyrope.Benchmarks] Triggering Index Build (Compaction)...");
            await BuildIndexAsync(options.HttpBaseUrl!, options.AdminApiKey!, options.TenantId, options.IndexName);
            Console.WriteLine("[Pyrope.Benchmarks] Build triggered.");
            // Wait a bit for build to finish? It's sync in the controller (calls index.Build() which locks), 
            // but the HTTP call waits for it.
            // But if it takes distinct time, fine.
            Console.WriteLine();
        }
        else if (options.BuildIndex)
        {
             Console.WriteLine("[Pyrope.Benchmarks] Warning: --build-index ignored because --http is missing.");
        }

        // 2) Warmup
        if (options.Warmup > 0)
        {
            Console.WriteLine($"[Pyrope.Benchmarks] Warmup: {options.Warmup} queries ...");
            for (var i = 0; i < options.Warmup; i++)
            {
                var q = encodedQueries[i % encodedQueries.Length];
                db.Execute("VEC.SEARCH", options.TenantId, options.IndexName, "TOPK", options.TopK.ToString(), "VECTOR", q, "API_KEY", options.TenantApiKey);
            }
            Console.WriteLine();
        }

        // 3) Benchmark
        Console.WriteLine($"[Pyrope.Benchmarks] Benchmark: queries={encodedQueries.Length} topK={options.TopK} concurrency={options.Concurrency} ...");
        var benchmark = await BenchmarkSearchAsync(
            db,
            options.TenantId,
            options.TenantApiKey!,
            options.IndexName,
            options.TopK,
            encodedQueries,
            options.Concurrency);

        var summary = LatencySummary.FromMilliseconds(benchmark.LatenciesMs);
        Console.WriteLine($"[Pyrope.Benchmarks] Dimension={dimension}");
        Console.WriteLine($"[Pyrope.Benchmarks] Total={summary.Count} elapsed={benchmark.Elapsed.TotalSeconds:F2}s QPS={benchmark.Qps:0.0}");
        Console.WriteLine($"[Pyrope.Benchmarks] Latency(ms): min={summary.MinMs:0.###} p50={summary.P50Ms:0.###} p95={summary.P95Ms:0.###} p99={summary.P99Ms:0.###} max={summary.MaxMs:0.###} mean={summary.MeanMs:0.###}");

        if (options.PrintStats)
        {
            try
            {
                var stats = db.Execute("VEC.STATS", options.TenantId, "API_KEY", options.TenantApiKey);
                Console.WriteLine();
                Console.WriteLine("[Pyrope.Benchmarks] VEC.STATS:");
                Console.WriteLine(stats.ToString());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Pyrope.Benchmarks] VEC.STATS failed: {ex.Message}");
            }
        }

        return 0;
    }

    private static (IEnumerable<float[]> BaseVectors, List<float[]> QueryVectors, int Dimension) LoadDataset(Options options)
    {
        switch (options.Dataset.ToLowerInvariant())
        {
            case "sift":
                {
                    var (basePath, queryPath) = ResolveSiftPaths(options);
                    var baseVectors = FvecsReader.Read(basePath, limit: options.BaseLimit);
                    var queries = FvecsReader.Read(queryPath, limit: options.QueryLimit).ToList();
                    var dim = queries.Count > 0 ? queries[0].Length : 0;
                    return (baseVectors, queries, dim);
                }
            case "glove":
                {
                    if (string.IsNullOrWhiteSpace(options.GlovePath))
                    {
                        throw new ArgumentException("glove dataset requires --glove-path");
                    }
                    if (options.Dimension <= 0)
                    {
                        throw new ArgumentException("glove dataset requires --dim");
                    }

                    var baseVectors = GloveTxtReader.Read(options.GlovePath!, options.Dimension, limit: options.BaseLimit, skipInvalidLines: true);
                    var queries = GloveTxtReader.Read(options.GlovePath!, options.Dimension, limit: options.QueryLimit, skipInvalidLines: true).ToList();
                    return (baseVectors, queries, options.Dimension);
                }
            case "synthetic":
                {
                    if (options.Dimension <= 0)
                    {
                        throw new ArgumentException("synthetic dataset requires --dim");
                    }
                    var baseVectors = GenerateRandomVectors(options.BaseLimit, options.Dimension, seed: 42);
                    var queries = GenerateRandomVectors(options.QueryLimit, options.Dimension, seed: 1337).ToList();
                    return (baseVectors, queries, options.Dimension);
                }
            default:
                throw new ArgumentException($"Unknown dataset: {options.Dataset}");
        }
    }

    private static (string BasePath, string QueryPath) ResolveSiftPaths(Options options)
    {
        if (!string.IsNullOrWhiteSpace(options.SiftBasePath) && !string.IsNullOrWhiteSpace(options.SiftQueryPath))
        {
            return (options.SiftBasePath!, options.SiftQueryPath!);
        }

        if (!string.IsNullOrWhiteSpace(options.SiftDir))
        {
            var basePath = Path.Combine(options.SiftDir!, "sift_base.fvecs");
            var queryPath = Path.Combine(options.SiftDir!, "sift_query.fvecs");
            return (basePath, queryPath);
        }

        throw new ArgumentException("sift dataset requires --sift-dir or both --sift-base and --sift-query.");
    }

    private static IEnumerable<float[]> GenerateRandomVectors(int count, int dimension, int seed)
    {
        var rng = new Random(seed);
        for (var i = 0; i < count; i++)
        {
            var v = new float[dimension];
            for (var d = 0; d < dimension; d++)
            {
                v[d] = (float)rng.NextDouble();
            }
            yield return v;
        }
    }

    private static async Task<LoadResult> LoadVectorsAsync(
        IDatabase db,
        string tenantId,
        string tenantApiKey,
        string indexName,
        IEnumerable<float[]> vectors,
        int concurrency,
        string idPrefix,
        bool useBinaryPayload)
    {
        if (concurrency <= 0) concurrency = 1;
        var bufferSize = Math.Max(32, concurrency * 8);

        var channel = Channel.CreateBounded<(int Id, float[] Vector)>(new BoundedChannelOptions(bufferSize)
        {
            SingleWriter = true,
            SingleReader = false,
            FullMode = BoundedChannelFullMode.Wait
        });

        var completedCount = 0;
        var totalToLoad = 0;
        var lastProgressTime = Stopwatch.GetTimestamp();
        var progressInterval = Stopwatch.Frequency; // 1秒ごと

        var workers = new List<Task>(concurrency);
        for (var w = 0; w < concurrency; w++)
        {
            workers.Add(Task.Run(async () =>
            {
                await foreach (var item in channel.Reader.ReadAllAsync())
                {
                    var id = $"{idPrefix}{item.Id}";
                    var payload = useBinaryPayload
                        ? (RedisValue)VectorEncoding.ToLittleEndianBytes(item.Vector)
                        : (RedisValue)JsonSerializer.Serialize(item.Vector);

                    db.Execute("VEC.UPSERT", tenantId, indexName, id, "VECTOR", payload, "API_KEY", tenantApiKey);

                    var completed = Interlocked.Increment(ref completedCount);
                    var now = Stopwatch.GetTimestamp();
                    if (now - lastProgressTime >= progressInterval || completed == totalToLoad)
                    {
                        var elapsed = (now - lastProgressTime) * 1000d / Stopwatch.Frequency;
                        var rate = elapsed > 0 ? completed / (elapsed / 1000d) : 0;
                        Console.Write($"\r[Pyrope.Benchmarks] Loading: {completed}/{totalToLoad} ({100.0 * completed / Math.Max(1, totalToLoad):F1}%) - {rate:0.0} vec/s");
                        lastProgressTime = now;
                    }
                }
            }));
        }

        var loaded = 0;
        var sw = Stopwatch.StartNew();
        try
        {
            // まず総数をカウント（IEnumerableの場合は推定）
            var vectorList = vectors as IList<float[]> ?? vectors.ToList();
            totalToLoad = vectorList.Count;

            foreach (var vector in vectorList)
            {
                await channel.Writer.WriteAsync((loaded, vector));
                loaded++;
            }
        }
        finally
        {
            channel.Writer.TryComplete();
        }

        await Task.WhenAll(workers);
        sw.Stop();
        Console.WriteLine(); // 進捗行の改行

        var throughput = sw.Elapsed.TotalSeconds <= 0 ? 0 : loaded / sw.Elapsed.TotalSeconds;
        return new LoadResult(loaded, sw.Elapsed, throughput);
    }

    private static async Task<SearchBenchmarkResult> BenchmarkSearchAsync(
        IDatabase db,
        string tenantId,
        string tenantApiKey,
        string indexName,
        int topK,
        IReadOnlyList<RedisValue> encodedQueries,
        int concurrency)
    {
        if (encodedQueries.Count == 0) throw new ArgumentException("No queries provided.", nameof(encodedQueries));
        if (concurrency <= 0) concurrency = 1;

        var latencies = new double[encodedQueries.Count];
        var startAll = Stopwatch.StartNew();
        var startTimestamp = Stopwatch.GetTimestamp();
        var completedCount = 0;
        var lastProgressTime = Stopwatch.GetTimestamp();
        var progressInterval = Stopwatch.Frequency; // 1秒ごと

        var workers = new List<Task>(concurrency);
        for (var w = 0; w < concurrency; w++)
        {
            var workerId = w;
            workers.Add(Task.Run(async () =>
            {
                for (var i = workerId; i < encodedQueries.Count; i += concurrency)
                {
                    var q = encodedQueries[i];
                    var t0 = Stopwatch.GetTimestamp();
                    db.Execute("VEC.SEARCH", tenantId, indexName, "TOPK", topK.ToString(), "VECTOR", q, "API_KEY", tenantApiKey);
                    var t1 = Stopwatch.GetTimestamp();
                    latencies[i] = (t1 - t0) * 1000d / Stopwatch.Frequency;

                    var completed = Interlocked.Increment(ref completedCount);
                    var now = Stopwatch.GetTimestamp();
                    if (now - lastProgressTime >= progressInterval || completed == encodedQueries.Count)
                    {
                        var elapsed = (now - startTimestamp) * 1000d / Stopwatch.Frequency;
                        var currentQps = elapsed > 0 ? completed / (elapsed / 1000d) : 0;
                        Console.Write($"\r[Pyrope.Benchmarks] Searching: {completed}/{encodedQueries.Count} ({100.0 * completed / encodedQueries.Count:F1}%) - {currentQps:0.0} QPS");
                        lastProgressTime = now;
                    }
                }
            }));
        }

        await Task.WhenAll(workers);
        startAll.Stop();
        Console.WriteLine(); // 進捗行の改行

        var qps = startAll.Elapsed.TotalSeconds <= 0 ? 0 : encodedQueries.Count / startAll.Elapsed.TotalSeconds;
        return new SearchBenchmarkResult(latencies, startAll.Elapsed, qps);
    }

    private static async Task DisableCacheAsync(string httpBaseUrl, string adminApiKey, bool flush)
    {
        // Expect e.g. http://127.0.0.1:5000
        var baseUri = httpBaseUrl.EndsWith("/", StringComparison.Ordinal) ? httpBaseUrl : httpBaseUrl + "/";
        using var http = new HttpClient { BaseAddress = new Uri(baseUri) };
        http.DefaultRequestHeaders.Add("X-API-KEY", adminApiKey);

        var payload = JsonSerializer.Serialize(new { enableCache = false, defaultTtlSeconds = 0 });
        using var put = new StringContent(payload, TextEncoding.UTF8, "application/json");
        var putRes = await http.PutAsync("v1/cache/policies", put);
        putRes.EnsureSuccessStatusCode();

        if (flush)
        {
            var postRes = await http.PostAsync("v1/cache/flush", content: null);
            postRes.EnsureSuccessStatusCode();
        }
    }

    private static async Task EnsureTenantAsync(string httpBaseUrl, string adminApiKey, string tenantId, string tenantApiKey)
    {
        var baseUri = httpBaseUrl.EndsWith("/", StringComparison.Ordinal) ? httpBaseUrl : httpBaseUrl + "/";
        using var http = new HttpClient { BaseAddress = new Uri(baseUri) };
        http.DefaultRequestHeaders.Add("X-API-KEY", adminApiKey);

        var payload = JsonSerializer.Serialize(new { tenantId = tenantId, apiKey = tenantApiKey });
        using var post = new StringContent(payload, TextEncoding.UTF8, "application/json");

        // Create if missing; if already exists, that's fine.
        var res = await http.PostAsync("v1/tenants", post);
        if (res.IsSuccessStatusCode)
        {
            return;
        }

        // If exists, update API key to expected value.
        if (res.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var putPayload = JsonSerializer.Serialize(new { apiKey = tenantApiKey });
            using var put = new StringContent(putPayload, TextEncoding.UTF8, "application/json");
            var putRes = await http.PutAsync($"v1/tenants/{tenantId}/apikey", put);
            putRes.EnsureSuccessStatusCode();
            return;
        }

        res.EnsureSuccessStatusCode();
    }

    private static async Task BuildIndexAsync(string httpBaseUrl, string adminApiKey, string tenantId, string indexName)
    {
        var baseUri = httpBaseUrl.EndsWith("/", StringComparison.Ordinal) ? httpBaseUrl : httpBaseUrl + "/";
        using var http = new HttpClient { BaseAddress = new Uri(baseUri) };
        http.DefaultRequestHeaders.Add("X-API-KEY", adminApiKey);

        // POST /v1/indexes/{tenantId}/{indexName}/build
        var res = await http.PostAsync($"v1/indexes/{tenantId}/{indexName}/build", content: null);
        res.EnsureSuccessStatusCode();
    }

    private static bool TryParseArgs(string[] args, out Options options, out string error)
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (!a.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var eq = a.IndexOf('=', StringComparison.Ordinal);
            if (eq > 0)
            {
                map[a[..eq]] = a[(eq + 1)..];
                continue;
            }

            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                map[a] = args[i + 1];
                i++;
                continue;
            }

            map[a] = null; // flag
        }

        options = new Options();

        options.Host = map.TryGetValue("--host", out var host) && !string.IsNullOrWhiteSpace(host) ? host! : options.Host;
        options.Port = TryGetInt(map, "--port", options.Port);
        options.TenantId = map.TryGetValue("--tenant", out var tenant) && !string.IsNullOrWhiteSpace(tenant) ? tenant! : options.TenantId;
        options.IndexName = map.TryGetValue("--index", out var index) && !string.IsNullOrWhiteSpace(index) ? index! : options.IndexName;
        options.Dataset = map.TryGetValue("--dataset", out var dataset) && !string.IsNullOrWhiteSpace(dataset) ? dataset! : options.Dataset;
        options.SiftDir = map.TryGetValue("--sift-dir", out var siftDir) ? siftDir : null;
        options.SiftBasePath = map.TryGetValue("--sift-base", out var siftBase) ? siftBase : null;
        options.SiftQueryPath = map.TryGetValue("--sift-query", out var siftQuery) ? siftQuery : null;
        options.GlovePath = map.TryGetValue("--glove-path", out var glovePath) ? glovePath : null;
        options.Dimension = TryGetInt(map, "--dim", options.Dimension);
        options.BaseLimit = TryGetInt(map, "--base-limit", options.BaseLimit);
        options.QueryLimit = TryGetInt(map, "--query-limit", options.QueryLimit);
        options.TopK = TryGetInt(map, "--topk", options.TopK);
        options.Concurrency = TryGetInt(map, "--concurrency", options.Concurrency);
        options.Warmup = TryGetInt(map, "--warmup", options.Warmup);
        options.HttpBaseUrl = map.TryGetValue("--http", out var http) ? http : null;
        options.CacheMode = map.TryGetValue("--cache", out var cache) && !string.IsNullOrWhiteSpace(cache) ? cache! : options.CacheMode;
        options.IdPrefix = map.TryGetValue("--id-prefix", out var idPrefix) && !string.IsNullOrWhiteSpace(idPrefix) ? idPrefix! : options.IdPrefix;
        options.PrintStats = map.ContainsKey("--print-stats");
        options.TenantApiKey = map.TryGetValue("--api-key", out var apiKey) ? apiKey : options.TenantApiKey;
        options.AdminApiKey = map.TryGetValue("--admin-api-key", out var adminKey) ? adminKey : options.AdminApiKey;
        options.UniqueQueries = TryGetInt(map, "--unique-queries", options.UniqueQueries);
        options.Repeat = TryGetInt(map, "--repeat", options.Repeat);
        options.Sequence = map.ContainsKey("--sequence");
        options.BuildIndex = map.ContainsKey("--build-index");

        var payload = map.TryGetValue("--payload", out var payloadValue) ? payloadValue : null;
        if (!string.IsNullOrWhiteSpace(payload))
        {
            options.UseBinaryPayload = payload.Equals("binary", StringComparison.OrdinalIgnoreCase);
            if (!options.UseBinaryPayload && !payload.Equals("json", StringComparison.OrdinalIgnoreCase))
            {
                error = "--payload must be 'binary' or 'json'.";
                return false;
            }
        }

        if (options.Port <= 0)
        {
            error = "--port must be positive.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(options.TenantId) || string.IsNullOrWhiteSpace(options.IndexName))
        {
            error = "--tenant and --index are required.";
            return false;
        }

        if (options.BaseLimit <= 0 || options.QueryLimit <= 0)
        {
            error = "--base-limit and --query-limit must be positive.";
            return false;
        }

        if (options.TopK <= 0)
        {
            error = "--topk must be positive.";
            return false;
        }

        error = "";
        return true;
    }

    private static int TryGetInt(Dictionary<string, string?> map, string key, int fallback)
    {
        if (!map.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        return int.TryParse(raw, out var value) ? value : fallback;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Pyrope.Benchmarks (P1-6 Vector Benchmark Tool)");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project src/Pyrope.Benchmarks -- <options>");
        Console.WriteLine();
        Console.WriteLine("Common options:");
        Console.WriteLine("  --host <host>                 (default: 127.0.0.1)");
        Console.WriteLine("  --port <port>                 (default: 3278)");
        Console.WriteLine("  --tenant <tenantId>           (default: tenant_bench)");
        Console.WriteLine("  --index <indexName>           (default: idx_bench)");
        Console.WriteLine("  --api-key <tenantApiKey>      (required) tenant API key for VEC.* commands");
        Console.WriteLine("  --payload <binary|json>       (default: binary)");
        Console.WriteLine("  --base-limit <n>              (default: 100000)");
        Console.WriteLine("  --query-limit <n>             (default: 1000)");
        Console.WriteLine("  --topk <k>                    (default: 10)");
        Console.WriteLine("  --concurrency <n>             (default: CPU count)");
        Console.WriteLine("  --warmup <n>                  (default: 100)");
        Console.WriteLine("  --print-stats                 (optional) prints VEC.STATS after benchmark");
        Console.WriteLine("  --unique-queries <n>          (optional) limit to N unique queries from dataset");
        Console.WriteLine("  --repeat <n>                  (optional) repeat unique queries to fill query-limit");
        Console.WriteLine("  --sequence                    (optional) use A, B, A, B sequence to test prefetching");
        Console.WriteLine();
        Console.WriteLine("Dataset: sift (SIFT1M fvecs)");
        Console.WriteLine("  --dataset sift --sift-dir <dir>            expects <dir>/sift_base.fvecs and <dir>/sift_query.fvecs");
        Console.WriteLine("  --dataset sift --sift-base <path> --sift-query <path>");
        Console.WriteLine();
        Console.WriteLine("Dataset: glove (GloVe txt)");
        Console.WriteLine("  --dataset glove --glove-path <path> --dim <dimension>");
        Console.WriteLine();
        Console.WriteLine("Cache control (optional, via HTTP Control Plane):");
        Console.WriteLine("  --http <baseUrl>              e.g. http://127.0.0.1:5000");
        Console.WriteLine("  --admin-api-key <adminKey>    (required with --http) admin API key for /v1/*");
        Console.WriteLine("  --cache <off|on>              (default: off) when off + --http, disables cache and flushes");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet run --project src/Pyrope.GarnetServer -- --port 3278 --bind 127.0.0.1");
        Console.WriteLine("  dotnet run --project src/Pyrope.Benchmarks -- --dataset sift --sift-dir ./datasets/sift1m --base-limit 100000 --query-limit 1000 --api-key <tenantApiKey>");
        Console.WriteLine();
    }

    private sealed class Options
    {
        public string Host { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 3278;
        public string TenantId { get; set; } = "tenant_bench";
        // Use unique index name per run to avoid conflicts with existing data
        public string IndexName { get; set; } = $"idx_bench_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        public string? TenantApiKey { get; set; }
        public string Dataset { get; set; } = "sift";
        public string? SiftDir { get; set; }
        public string? SiftBasePath { get; set; }
        public string? SiftQueryPath { get; set; }
        public string? GlovePath { get; set; }
        public int Dimension { get; set; }
        public int BaseLimit { get; set; } = 100_000;
        public int QueryLimit { get; set; } = 1_000;
        public int TopK { get; set; } = 10;
        public int Concurrency { get; set; } = Math.Max(1, Environment.ProcessorCount);
        public int Warmup { get; set; } = 100;
        public bool UseBinaryPayload { get; set; } = true;
        public string? HttpBaseUrl { get; set; }
        public string? AdminApiKey { get; set; }
        public string CacheMode { get; set; } = "off";
        public string IdPrefix { get; set; } = "v";
        public bool PrintStats { get; set; }
        public int UniqueQueries { get; set; }
        public int Repeat { get; set; } = 1;
        public bool Sequence { get; set; }
        public bool BuildIndex { get; set; }
    }

    private sealed record LoadResult(int Loaded, TimeSpan Elapsed, double Throughput);

    private sealed record SearchBenchmarkResult(double[] LatenciesMs, TimeSpan Elapsed, double Qps);
}
