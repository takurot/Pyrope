namespace Pyrope.GarnetServer.Services
{
    public sealed class SloGuardrailsOptions
    {
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// When estimated P99 latency exceeds this threshold, search is degraded.
        /// </summary>
        public double TargetP99Ms { get; set; } = 50;

        /// <summary>
        /// Hysteresis for recovery. Degradation is cleared when P99 <= TargetP99Ms * RecoveryFactor.
        /// </summary>
        public double RecoveryFactor { get; set; } = 0.8;

        /// <summary>
        /// Search budget used while degraded (BruteForce: max number of vectors scanned).
        /// </summary>
        public int DegradedMaxScans { get; set; } = 5000;

        /// <summary>
        /// Monitoring interval (seconds) for estimating P99 from the latency histogram.
        /// </summary>
        public int MonitorIntervalSeconds { get; set; } = 5;

        /// <summary>
        /// Minimum number of samples per interval to consider P99 estimate stable enough.
        /// </summary>
        public int MinSamplesPerInterval { get; set; } = 20;
    }
}

