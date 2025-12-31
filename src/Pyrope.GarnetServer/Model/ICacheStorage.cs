using System;

namespace Pyrope.GarnetServer.Model
{
    public interface ICacheStorage
    {
        bool TryGet(string key, out byte[]? value);
        void Set(string key, byte[] value, TimeSpan? ttl = null);
    }
}
