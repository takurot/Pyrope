using System;

namespace Pyrope.GarnetServer.Model
{
    public sealed record TenantBillingUsage(
        string TenantId,
        long RequestsTotal,
        long CacheHits,
        long CacheMisses,
        decimal ComputeCostUnits,
        decimal ComputeSeconds,
        long VectorStorageBytes,
        long SnapshotStorageBytes,
        DateTimeOffset UpdatedAt);
}
