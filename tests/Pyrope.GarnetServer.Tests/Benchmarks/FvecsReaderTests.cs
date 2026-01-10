using System.IO;
using System.Linq;
using Pyrope.Benchmarks.Datasets;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Benchmarks;

public sealed class FvecsReaderTests
{
    [Fact]
    public void Read_AllVectors_ReturnsExpectedVectors()
    {
        var path = Path.GetTempFileName();
        try
        {
            using (var stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.Read))
            using (var writer = new BinaryWriter(stream))
            {
                WriteFvec(writer, new[] { 1.0f, 2.0f, 3.0f });
                WriteFvec(writer, new[] { 4.0f, 5.0f, 6.0f });
            }

            var vectors = FvecsReader.Read(path).ToList();

            Assert.Equal(2, vectors.Count);
            Assert.Equal(new[] { 1.0f, 2.0f, 3.0f }, vectors[0]);
            Assert.Equal(new[] { 4.0f, 5.0f, 6.0f }, vectors[1]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Read_WithLimit_ReturnsOnlyFirstN()
    {
        var path = Path.GetTempFileName();
        try
        {
            using (var stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.Read))
            using (var writer = new BinaryWriter(stream))
            {
                WriteFvec(writer, new[] { 1.0f, 2.0f, 3.0f });
                WriteFvec(writer, new[] { 4.0f, 5.0f, 6.0f });
            }

            var vectors = FvecsReader.Read(path, limit: 1).ToList();

            Assert.Single(vectors);
            Assert.Equal(new[] { 1.0f, 2.0f, 3.0f }, vectors[0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void WriteFvec(BinaryWriter writer, float[] vector)
    {
        writer.Write(vector.Length);
        foreach (var value in vector)
        {
            writer.Write(value);
        }
    }
}

