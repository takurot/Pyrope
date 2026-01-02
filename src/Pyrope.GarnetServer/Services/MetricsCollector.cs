using System;
using System.Text;
using System.Threading;

namespace Pyrope.GarnetServer.Services
{
    public class MetricsCollector : IMetricsCollector
    {
        private long _cacheHits;
        private long _cacheMisses;
        private long _evictions;
        private long _aiFallbacks;

        // Simple latency buckets (count)
        // <1ms, <5ms, <10ms, <50ms, <100ms, >100ms
        private readonly long[] _latencyBuckets = new long[6];

        public void RecordCacheHit()
        {
            Interlocked.Increment(ref _cacheHits);
        }

        public void RecordCacheMiss()
        {
            Interlocked.Increment(ref _cacheMisses);
        }

        public void RecordEviction(string reason)
        {
            // For MVP just counting total evictions, reason is logged or ignored for agg.
            Interlocked.Increment(ref _evictions);
        }

        public void RecordAiFallback()
        {
            Interlocked.Increment(ref _aiFallbacks);
        }

        public void RecordSearchLatency(TimeSpan duration)
        {
            var ms = duration.TotalMilliseconds;
            int bucketIndex;

            if (ms < 1) bucketIndex = 0;
            else if (ms < 5) bucketIndex = 1;
            else if (ms < 10) bucketIndex = 2;
            else if (ms < 50) bucketIndex = 3;
            else if (ms < 100) bucketIndex = 4;
            else bucketIndex = 5;

            Interlocked.Increment(ref _latencyBuckets[bucketIndex]);
        }

        public string GetStats()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# HELP cache_hit_total Total number of cache hits");
            sb.AppendLine($"# TYPE cache_hit_total counter");
            sb.AppendLine($"cache_hit_total {Interlocked.Read(ref _cacheHits)}");

            sb.AppendLine($"# HELP cache_miss_total Total number of cache misses");
            sb.AppendLine($"# TYPE cache_miss_total counter");
            sb.AppendLine($"cache_miss_total {Interlocked.Read(ref _cacheMisses)}");

            sb.AppendLine($"# HELP cache_eviction_total Total number of cache evictions");
            sb.AppendLine($"# TYPE cache_eviction_total counter");
            sb.AppendLine($"cache_eviction_total {Interlocked.Read(ref _evictions)}");

            sb.AppendLine($"# HELP ai_fallback_total Total number of AI fallback events");
            sb.AppendLine($"# TYPE ai_fallback_total counter");
            sb.AppendLine($"ai_fallback_total {Interlocked.Read(ref _aiFallbacks)}");

            sb.AppendLine($"# HELP vector_search_latency_ms Latency buckets");
            sb.AppendLine($"# TYPE vector_search_latency_ms histogram");

            long count = 0;

            count += Interlocked.Read(ref _latencyBuckets[0]);
            sb.AppendLine($"vector_search_latency_ms_bucket{{le=\"1\"}} {count}");

            count += Interlocked.Read(ref _latencyBuckets[1]);
            sb.AppendLine($"vector_search_latency_ms_bucket{{le=\"5\"}} {count}");

            count += Interlocked.Read(ref _latencyBuckets[2]);
            sb.AppendLine($"vector_search_latency_ms_bucket{{le=\"10\"}} {count}");

            count += Interlocked.Read(ref _latencyBuckets[3]);
            sb.AppendLine($"vector_search_latency_ms_bucket{{le=\"50\"}} {count}");

            count += Interlocked.Read(ref _latencyBuckets[4]);
            sb.AppendLine($"vector_search_latency_ms_bucket{{le=\"100\"}} {count}");

            count += Interlocked.Read(ref _latencyBuckets[5]);
            sb.AppendLine($"vector_search_latency_ms_bucket{{le=\"+Inf\"}} {count}");

            sb.AppendLine($"vector_search_latency_ms_count {count}");
            sb.AppendLine($"vector_search_latency_ms_sum 0"); // Sum not implemented yet

            return sb.ToString();
        }

        public MetricsSnapshot GetSnapshot()
        {
            var buckets = new long[_latencyBuckets.Length];
            for (int i = 0; i < _latencyBuckets.Length; i++)
            {
                buckets[i] = Interlocked.Read(ref _latencyBuckets[i]);
            }

            return new MetricsSnapshot(
                Interlocked.Read(ref _cacheHits),
                Interlocked.Read(ref _cacheMisses),
                Interlocked.Read(ref _evictions),
                Interlocked.Read(ref _aiFallbacks),
                buckets);
        }

        public void Reset()
        {
            Interlocked.Exchange(ref _cacheHits, 0);
            Interlocked.Exchange(ref _cacheMisses, 0);
            Interlocked.Exchange(ref _evictions, 0);
            Interlocked.Exchange(ref _aiFallbacks, 0);
            for (int i = 0; i < _latencyBuckets.Length; i++)
            {
                Interlocked.Exchange(ref _latencyBuckets[i], 0);
            }
        }
    }
}
