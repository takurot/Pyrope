using System;
using System.Runtime.InteropServices;

namespace Pyrope.Benchmarks.Encoding;

public static class VectorEncoding
{
    public static byte[] ToLittleEndianBytes(float[] vector)
    {
        if (vector is null) throw new ArgumentNullException(nameof(vector));

        var bytes = new byte[checked(vector.Length * sizeof(float))];
        var src = MemoryMarshal.AsBytes(vector.AsSpan());
        src.CopyTo(bytes);
        return bytes;
    }
}

