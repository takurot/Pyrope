using System;

namespace Pyrope.GarnetServer.Model
{
    public sealed record BillingLogEntry(
        string EntryId,
        DateTimeOffset Timestamp,
        string TenantId,
        long RequestsTotal,
        long CacheHits,
        long CacheMisses,
        double ComputeCostUnits,
        double ComputeSeconds,
        long VectorStorageBytes,
        long SnapshotStorageBytes,
        string PrevHash,
        string Hash);
}
