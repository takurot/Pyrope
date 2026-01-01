using System;
using Pyrope.GarnetServer.Services;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Services
{
    public class MetricsCollectorTests
    {
        [Fact]
        public void RecordAndGetStats_ShouldReturnCorrectCounts()
        {
            var collector = new MetricsCollector();

            collector.RecordCacheHit();
            collector.RecordCacheHit();
            collector.RecordCacheMiss();
            collector.RecordEviction("ttl");

            var stats = collector.GetStats();

            Assert.Contains("cache_hit_total 2", stats);
            Assert.Contains("cache_miss_total 1", stats);
            Assert.Contains("cache_eviction_total 1", stats);
        }

        [Fact]
        public void RecordLatency_ShouldBucketCorrectly()
        {
            var collector = new MetricsCollector();

            collector.RecordSearchLatency(TimeSpan.FromMilliseconds(0.5)); // < 1
            collector.RecordSearchLatency(TimeSpan.FromMilliseconds(4));   // < 5
            collector.RecordSearchLatency(TimeSpan.FromMilliseconds(150)); // > 100

            var stats = collector.GetStats();

            Assert.Contains("vector_search_latency_ms_bucket{le=\"1\"} 1", stats);
            Assert.Contains("vector_search_latency_ms_bucket{le=\"5\"} 2", stats);
            Assert.Contains("vector_search_latency_ms_bucket{le=\"+Inf\"} 3", stats);
            Assert.Contains("vector_search_latency_ms_bucket{le=\"50\"} 2", stats);
        }

        [Fact]
        public void Reset_ShouldClearStats()
        {
            var collector = new MetricsCollector();
            collector.RecordCacheHit();

            collector.Reset();

            var stats = collector.GetStats();
            Assert.Contains("cache_hit_total 0", stats);
        }

        [Fact]
        public void GetSnapshot_ShouldReturnTotals()
        {
            var collector = new MetricsCollector();
            collector.RecordCacheHit();
            collector.RecordCacheMiss();
            collector.RecordEviction("ttl");
            collector.RecordSearchLatency(TimeSpan.FromMilliseconds(2));

            var snapshot = collector.GetSnapshot();

            Assert.Equal(1, snapshot.CacheHits);
            Assert.Equal(1, snapshot.CacheMisses);
            Assert.Equal(1, snapshot.Evictions);
            Assert.Equal(1, snapshot.LatencyBuckets[1]);
        }
    }
}
