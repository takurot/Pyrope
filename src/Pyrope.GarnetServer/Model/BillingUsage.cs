using System;

namespace Pyrope.GarnetServer.Model
{
    public sealed record TenantBillingUsage(
        string TenantId,
        long RequestsTotal,
        long CacheHits,
        long CacheMisses,
        double ComputeCostUnits,
        double ComputeSeconds,
        long VectorStorageBytes,
        long SnapshotStorageBytes,
        DateTimeOffset UpdatedAt);
}
