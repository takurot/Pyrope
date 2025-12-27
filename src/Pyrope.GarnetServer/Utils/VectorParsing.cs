using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Pyrope.GarnetServer.Utils
{
    public static class VectorParsing
    {
        public static float[] ParseVector(ReadOnlySpan<byte> data)
        {
            if (data.IsEmpty)
            {
                throw new ArgumentException("Vector payload is empty.", nameof(data));
            }

            var text = Encoding.UTF8.GetString(data);
            if (TryParseJsonVector(text, out var jsonVector))
            {
                return jsonVector;
            }

            if (TryParseCsvVector(text, out var csvVector))
            {
                return csvVector;
            }

            if (data.Length % sizeof(float) == 0)
            {
                return ParseBinaryVector(data);
            }

            throw new FormatException("Unsupported vector format.");
        }

        private static bool TryParseJsonVector(string text, out float[] vector)
        {
            vector = Array.Empty<float>();
            if (string.IsNullOrWhiteSpace(text) || text[0] != '[')
            {
                return false;
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<float[]>(text);
                if (parsed == null || parsed.Length == 0)
                {
                    return false;
                }

                vector = parsed;
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static bool TryParseCsvVector(string text, out float[] vector)
        {
            vector = Array.Empty<float>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var separators = new[] { ',', ' ' };
            var parts = text.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                return false;
            }

            var values = new float[parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    return false;
                }
                values[i] = value;
            }

            vector = values;
            return true;
        }

        private static float[] ParseBinaryVector(ReadOnlySpan<byte> data)
        {
            var floats = MemoryMarshal.Cast<byte, float>(data);
            var vector = new float[floats.Length];
            floats.CopyTo(vector);
            return vector;
        }
    }
}
