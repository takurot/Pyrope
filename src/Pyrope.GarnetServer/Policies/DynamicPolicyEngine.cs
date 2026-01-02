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

        public void UpdatePolicy(Pyrope.Policy.WarmPathPolicy policy)
        {
            var newConfig = new CachePolicyConfig
            {
                EnableCache = true, // Default to true if receiving policy, or add logic to disable based on sidecar
                DefaultTtlSeconds = policy.TtlSeconds
            };

            // Note: WarmPathPolicy also has AdmissionThreshold and EvictionPriority.
            // These are not yet in CachePolicyConfig. We should probably add them to CachePolicyConfig 
            // if we want to support them, but for now we map what we have.

            _store.Update(newConfig);
        }
    }
}
