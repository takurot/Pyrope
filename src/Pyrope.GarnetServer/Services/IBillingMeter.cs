using System.Collections.Generic;
using Pyrope.GarnetServer.Model;

namespace Pyrope.GarnetServer.Services
{
    public interface IBillingMeter
    {
        void RecordRequest(string tenantId, bool cacheHit);
        void RecordCompute(string tenantId, double costUnits);
        void RecordVectorBytes(string tenantId, long deltaBytes);
        void RecordSnapshot(string tenantId, string indexName, string snapshotPath);
        bool TryGetUsage(string tenantId, out TenantBillingUsage usage);
        IReadOnlyCollection<TenantBillingUsage> GetAllUsage();
    }
}
