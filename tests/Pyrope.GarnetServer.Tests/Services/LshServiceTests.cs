using System;
using Pyrope.GarnetServer.Services;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Services
{
    public class LshServiceTests
    {
        [Fact]
        public void GenerateSimHash_ShouldBeDeterministic()
        {
            var service = new LshService(seed: 42, hashBits: 64);
            var vector = new float[128];
            Array.Fill(vector, 0.5f);
            vector[0] = 1.0f;

            var hash1 = service.GenerateSimHash(vector);
            var hash2 = service.GenerateSimHash(vector);

            Assert.Equal(hash1, hash2);
        }

        [Fact]
        public void GenerateSimHash_ShouldHaveLocalityProperty()
        {
            var service = new LshService(seed: 123, hashBits: 64);
            
            // v1 and v2 are very similar (angle is small)
            var v1 = new float[] { 1f, 0.9f, 0f, 0f };
            var v2 = new float[] { 1f, 0.95f, 0.01f, 0f };

            // v3 is orthogonal/dissimilar
            var v3 = new float[] { 0f, 0f, 1f, 1f };

            var hash1 = service.GenerateSimHash(v1);
            var hash2 = service.GenerateSimHash(v2);
            var hash3 = service.GenerateSimHash(v3);

            // Check Hamming Distance
            var dist12 = System.Numerics.BitOperations.PopCount((ulong)(hash1 ^ hash2));
            var dist13 = System.Numerics.BitOperations.PopCount((ulong)(hash1 ^ hash3));

            // v1 and v2 are very similar -> Distance should be very small (allow small margin for random plane cuts)
            Assert.True(dist12 <= 5, $"Expected small hamming distance for similar vectors, got {dist12}");

            // v1 and v3 are orthogonal -> Distance should be large (around 32 for 64-bit hash)
            Assert.True(dist13 > 10, $"Expected large hamming distance for orthogonal vectors, got {dist13}");
        }
    }
}
