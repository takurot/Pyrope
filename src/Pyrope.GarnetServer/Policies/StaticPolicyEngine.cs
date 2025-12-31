using System;
using Pyrope.GarnetServer.Model;

namespace Pyrope.GarnetServer.Policies
{
    public class StaticPolicyEngine : IPolicyEngine
    {
        private readonly TimeSpan _defaultTtl;

        public StaticPolicyEngine(TimeSpan defaultTtl)
        {
            _defaultTtl = defaultTtl;
        }

        public PolicyDecision Evaluate(QueryKey key)
        {
            // Simple static rule: Always cache with default TTL
            return PolicyDecision.Cache(_defaultTtl);
        }
    }
}
