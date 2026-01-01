using System;
using System.Diagnostics;

namespace Pyrope.GarnetServer.Services
{
    public readonly record struct SystemUsageSnapshot(DateTimeOffset Timestamp, TimeSpan CpuTime);

    public interface ISystemUsageProvider
    {
        int ProcessorCount { get; }
        SystemUsageSnapshot GetSnapshot();
    }

    public sealed class SystemUsageProvider : ISystemUsageProvider
    {
        public int ProcessorCount { get; } = Environment.ProcessorCount;

        public SystemUsageSnapshot GetSnapshot()
        {
            var now = DateTimeOffset.UtcNow;
            var cpu = Process.GetCurrentProcess().TotalProcessorTime;
            return new SystemUsageSnapshot(now, cpu);
        }
    }
}
