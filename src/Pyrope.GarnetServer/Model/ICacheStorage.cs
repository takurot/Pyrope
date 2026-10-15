using System;

namespace Pyrope.GarnetServer.Model
{
    public interface ICacheStorage
    {
        bool TryGet(string key, out byte[]? value);
        bool Set(string key, byte[] value, TimeSpan? ttl = null);
    }
}
