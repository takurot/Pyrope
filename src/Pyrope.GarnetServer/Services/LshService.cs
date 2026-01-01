using System;

namespace Pyrope.GarnetServer.Services
{
    public class LshService
    {
        private readonly int _seed;
        private readonly int _hashBits;

        // Cache projections per dimension: <dimension, projections>
        private readonly Dictionary<int, float[][]> _projectionCache = new();
        private readonly object _lock = new();

        public LshService(int seed = 42, int hashBits = 64)
        {
            if (hashBits > 64) throw new ArgumentOutOfRangeException(nameof(hashBits), "Max 64 bits supported");
            _seed = seed;
            _hashBits = hashBits;
        }

        private float[][] GetOrGenerateProjections(int dimensions)
        {
            lock (_lock)
            {
                if (_projectionCache.TryGetValue(dimensions, out var cached))
                {
                    return cached;
                }

                // Create deterministic RNG for this dimension
                // Using seed + dimension ensuring independent streams per dim
                var rng = new Random(_seed + dimensions);
                var projections = new float[_hashBits][];

                for (int i = 0; i < _hashBits; i++)
                {
                    projections[i] = new float[dimensions];
                    for (int j = 0; j < dimensions; j++)
                    {
                        projections[i][j] = (float)(rng.NextDouble() * 2.0 - 1.0);
                    }
                }

                _projectionCache[dimensions] = projections;
                return projections;
            }
        }

        public long GenerateSimHash(float[] vector)
        {
            var dim = vector.Length;
            var projections = GetOrGenerateProjections(dim);

            long hash = 0;
            for (int i = 0; i < _hashBits; i++)
            {
                double dot = 0;
                var plane = projections[i];
                for (int j = 0; j < dim; j++)
                {
                    dot += vector[j] * plane[j];
                }

                if (dot > 0)
                {
                    hash |= (1L << i);
                }
            }
            return hash;
        }
    }
}
