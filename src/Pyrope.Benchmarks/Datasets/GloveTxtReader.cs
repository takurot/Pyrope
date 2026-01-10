using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Pyrope.Benchmarks.Datasets;

/// <summary>
/// Reader for GloVe-style text embeddings: "token v1 v2 ... vN".
/// </summary>
public static class GloveTxtReader
{
    public static IEnumerable<float[]> Read(string path, int dimension, int? limit = null, bool skipInvalidLines = false)
    {
        if (path is null) throw new ArgumentNullException(nameof(path));
        if (dimension <= 0) throw new ArgumentOutOfRangeException(nameof(dimension), "dimension must be positive.");
        if (limit is <= 0) yield break;

        var count = 0;
        foreach (var line in File.ReadLines(path))
        {
            if (limit.HasValue && count >= limit.Value)
            {
                yield break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            // GloVe typically uses spaces, but tolerate multiple spaces/tabs.
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < dimension + 1)
            {
                if (skipInvalidLines) continue;
                throw new InvalidDataException($"Invalid GloVe line (expected {dimension + 1} tokens, got {parts.Length}).");
            }

            // Ignore the first token (word).
            var vector = new float[dimension];
            var ok = true;
            for (var i = 0; i < dimension; i++)
            {
                if (!float.TryParse(parts[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    ok = false;
                    break;
                }
                vector[i] = value;
            }

            if (!ok)
            {
                if (skipInvalidLines) continue;
                throw new InvalidDataException("Invalid float value in GloVe line.");
            }

            yield return vector;
            count++;
        }
    }
}

