using System;
using Pyrope.GarnetServer.Model;

namespace Pyrope.GarnetServer.Policies
{
    public class DynamicPolicyEngine : IPolicyEngine
    {
        private readonly CachePolicyStore _store;

        public DynamicPolicyEngine(CachePolicyStore store)
        {
            _store = store;
        }

        public PolicyDecision Evaluate(QueryKey key)
        {
            var config = _store.Current;
            if (!config.EnableCache)
            {
                return PolicyDecision.NoCache;
            }

            var ttlSeconds = config.DefaultTtlSeconds;
            if (ttlSeconds <= 0)
            {
                return PolicyDecision.NoCache;
            }

            return PolicyDecision.Cache(TimeSpan.FromSeconds(ttlSeconds));
        }
    }
}
