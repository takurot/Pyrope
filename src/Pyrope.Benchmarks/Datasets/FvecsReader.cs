using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Pyrope.Benchmarks.Datasets;

/// <summary>
/// Reader for FAISS-style *.fvecs files.
/// Each record is: int32 dimension (d), followed by d float32 values (little-endian).
/// </summary>
public static class FvecsReader
{
    public static IEnumerable<float[]> Read(string path, int? limit = null)
    {
        if (path is null) throw new ArgumentNullException(nameof(path));
        if (limit is <= 0) yield break;

        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream);

        var count = 0;
        while (stream.Position < stream.Length)
        {
            if (limit.HasValue && count >= limit.Value)
            {
                yield break;
            }

            int dim;
            try
            {
                dim = reader.ReadInt32();
            }
            catch (EndOfStreamException)
            {
                yield break;
            }

            if (dim <= 0)
            {
                throw new InvalidDataException($"Invalid vector dimension {dim} in fvecs file.");
            }

            var byteCount = checked(dim * sizeof(float));
            var bytes = reader.ReadBytes(byteCount);
            if (bytes.Length != byteCount)
            {
                throw new EndOfStreamException("Truncated fvecs record.");
            }

            var floats = MemoryMarshal.Cast<byte, float>(bytes);
            var vector = floats.ToArray();
            yield return vector;
            count++;
        }
    }
}

