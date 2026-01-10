using Pyrope.GarnetServer.Vector;

namespace Pyrope.GarnetServer.Services
{
    public interface ISloGuardrails
    {
        bool IsDegraded { get; }
        double LastP99Ms { get; }

        SearchOptions? GetSearchOptions(string tenantId, string indexName);
        bool ShouldForceCacheOnly(string tenantId, string indexName);
        void UpdateLatencyP99(double p99Ms);
    }
}

