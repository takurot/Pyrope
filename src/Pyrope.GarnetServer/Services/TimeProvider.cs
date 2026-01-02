using System;

namespace Pyrope.GarnetServer.Services
{
    public interface ITimeProvider
    {
        long GetUnixTimeSeconds();
    }

    public sealed class SystemTimeProvider : ITimeProvider
    {
        public long GetUnixTimeSeconds()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
    }
}
