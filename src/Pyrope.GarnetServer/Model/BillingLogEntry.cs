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
        decimal ComputeCostUnits,
        decimal ComputeSeconds,
        long VectorStorageBytes,
        long SnapshotStorageBytes,
        string PrevHash,
        string Hash);
}
