using System.Collections.Generic;

namespace Pyrope.GarnetServer.Model
{
    public interface ICacheUsageProvider
    {
        long GetTenantUsageBytes(string tenantId);
        IReadOnlyDictionary<string, long> GetAllTenantUsageBytes();
    }
}
