using System.IO;
using System.Linq;
using Pyrope.Benchmarks.Datasets;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Benchmarks;

public sealed class GloveTxtReaderTests
{
    [Fact]
    public void Read_ParsesVectors_IgnoresTokens()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "word1 0.1 0.2 0.3\nword2 1 2 3\n");

            var vectors = GloveTxtReader.Read(path, dimension: 3, limit: 2, skipInvalidLines: false).ToList();

            Assert.Equal(2, vectors.Count);
            Assert.Equal(new[] { 0.1f, 0.2f, 0.3f }, vectors[0]);
            Assert.Equal(new[] { 1.0f, 2.0f, 3.0f }, vectors[1]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Read_WhenInvalidLineAndSkipInvalidLinesTrue_SkipsIt()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "word1 0.1 0.2 0.3\nbad 0.1 0.2\nword2 1 2 3\n");

            var vectors = GloveTxtReader.Read(path, dimension: 3, limit: 10, skipInvalidLines: true).ToList();

            Assert.Equal(2, vectors.Count);
            Assert.Equal(new[] { 0.1f, 0.2f, 0.3f }, vectors[0]);
            Assert.Equal(new[] { 1.0f, 2.0f, 3.0f }, vectors[1]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

