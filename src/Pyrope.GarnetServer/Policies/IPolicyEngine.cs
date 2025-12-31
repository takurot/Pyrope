using System;
using Pyrope.GarnetServer.Model;

namespace Pyrope.GarnetServer.Policies
{
    public struct PolicyDecision
    {
        public bool ShouldCache { get; init; }
        public TimeSpan? Ttl { get; init; }
        
        public static readonly PolicyDecision NoCache = new() { ShouldCache = false };
        public static PolicyDecision Cache(TimeSpan ttl) => new() { ShouldCache = true, Ttl = ttl };
    }

    public interface IPolicyEngine
    {
        PolicyDecision Evaluate(QueryKey key);
    }
}
