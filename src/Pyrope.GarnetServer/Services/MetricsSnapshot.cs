namespace Pyrope.GarnetServer.Services
{
    public readonly record struct MetricsSnapshot(
        long CacheHits,
        long CacheMisses,
        long Evictions,
        long AiFallbacks,
        long[] LatencyBuckets);
}
