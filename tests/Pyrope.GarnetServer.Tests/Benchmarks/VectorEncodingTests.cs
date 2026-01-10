using System;
using Pyrope.Benchmarks.Encoding;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Benchmarks;

public sealed class VectorEncodingTests
{
    [Fact]
    public void ToLittleEndianBytes_EncodesFloat32()
    {
        if (!BitConverter.IsLittleEndian)
        {
            return;
        }

        var bytes = VectorEncoding.ToLittleEndianBytes(new[] { 1.0f, 2.0f });
        var expected = new byte[8];
        Buffer.BlockCopy(BitConverter.GetBytes(1.0f), 0, expected, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(2.0f), 0, expected, 4, 4);

        Assert.Equal(expected, bytes);
    }
}

