using System;
using System.Collections.Generic;
using Pyrope.GarnetServer.Model;

namespace Pyrope.GarnetServer.Services
{
    public interface IBillingLogStore
    {
        BillingLogEntry AppendSnapshot(TenantBillingUsage usage, DateTimeOffset timestamp);
        IReadOnlyList<BillingLogEntry> Query(string tenantId, int limit = 100);
        bool VerifyChain(string tenantId);
    }
}
