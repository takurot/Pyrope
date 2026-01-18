using System;
using System.Linq;
using Xunit;
using Pyrope.GarnetServer.Vector;

namespace Pyrope.GarnetServer.Tests.Vector
{
    public class ScalarQuantizerTests
    {
        [Fact]
        public void QuantizeAndDequantize_RoundTrip()
        {
            var original = new float[] { 0.0f, 0.5f, 1.0f, -1.0f };
            // Expected Range: -1.0 to 1.0
            
            var quantized = ScalarQuantizer.Quantize(original, out float min, out float max);
            
            Assert.Equal(original.Length, quantized.Length);
            Assert.Equal(-1.0f, min);
            Assert.Equal(1.0f, max);

            var reconstructed = ScalarQuantizer.Dequantize(quantized, min, max);

            // Precision loss is expected with 8-bit. Tolerance ~1% of range (2.0 * 0.01 = 0.02)
            for (int i = 0; i < original.Length; i++)
            {
                Assert.Equal(original[i], reconstructed[i], 0.02f);
            }
        }

        [Fact]
        public void Quantize_HandlesFlatVector()
        {
            var original = new float[] { 0.5f, 0.5f, 0.5f };
            var quantized = ScalarQuantizer.Quantize(original, out float min, out float max);
            
            Assert.Equal(0.5f, min);
            Assert.Equal(0.5f, max);
            // If min == max, all bytes should be 0 or 127 or something consistent.
            Assert.All(quantized, b => Assert.Equal(0, b)); 
            
            var reconstructed = ScalarQuantizer.Dequantize(quantized, min, max);
            Assert.All(reconstructed, f => Assert.Equal(0.5f, f));
        }
    }
}
