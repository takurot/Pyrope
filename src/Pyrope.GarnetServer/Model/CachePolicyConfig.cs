namespace Pyrope.GarnetServer.Model
{
    public sealed class CachePolicyConfig
    {
        public bool EnableCache { get; set; } = true;
        public int DefaultTtlSeconds { get; set; } = 60;
    }
}
