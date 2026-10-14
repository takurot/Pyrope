namespace Pyrope.GarnetServer.Services
{
    public sealed class BillingOptions
    {
        public decimal CostUnitSeconds { get; set; } = 1.0m;
        public int LogIntervalSeconds { get; set; } = 60;
        public string? LogPath { get; set; }
        public int MaxInMemoryEntries { get; set; } = 10000;
    }
}
