using System;

namespace Pyrope.GarnetServer.Services
{
    public interface IMetricsCollector
    {
        void RecordCacheHit();
        void RecordCacheMiss();
        void RecordSearchLatency(TimeSpan duration);
        void RecordEviction(string reason);
        void RecordAiFallback();
        string GetStats();
        MetricsSnapshot GetSnapshot();
        void Reset();
    }
}
