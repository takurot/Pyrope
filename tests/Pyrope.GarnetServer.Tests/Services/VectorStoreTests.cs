using System.Collections.Generic;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Services;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Services
{
    public class VectorStoreTests
    {
        [Fact]
        public void TryAdd_RejectsDuplicateRecord()
        {
            var store = new VectorStore();
            var record = new VectorRecord(
                "tenant",
                "index",
                "doc1",
                new[] { 1f, 2f },
                null,
                new List<string>(),
                new Dictionary<string, double>(),
                default,
                default);

            Assert.True(store.TryAdd(record));
            Assert.False(store.TryAdd(record));
        }

        [Fact]
        public void Upsert_OverwritesExistingRecord()
        {
            var store = new VectorStore();
            var initial = new VectorRecord(
                "tenant",
                "index",
                "doc1",
                new[] { 1f, 2f },
                "{\"v\":1}",
                new List<string> { "t1" },
                new Dictionary<string, double> { { "score", 1 } },
                default,
                default);

            store.Upsert(initial);

            var updated = new VectorRecord(
                "tenant",
                "index",
                "doc1",
                new[] { 3f, 4f },
                "{\"v\":2}",
                new List<string> { "t2" },
                new Dictionary<string, double> { { "score", 2 } },
                default,
                default);

            store.Upsert(updated);

            Assert.True(store.TryGet("tenant", "index", "doc1", out var stored));
            Assert.Equal(3f, stored.Vector[0]);
            Assert.Equal("{\"v\":2}", stored.MetaJson);
            Assert.Equal("t2", stored.Tags[0]);
            Assert.Equal(2, stored.NumericFields["score"]);
        }

        [Fact]
        public void TryMarkDeleted_FlagsRecordAsDeleted()
        {
            var store = new VectorStore();
            var record = new VectorRecord(
                "tenant",
                "index",
                "doc1",
                new[] { 1f, 2f },
                null,
                new List<string>(),
                new Dictionary<string, double>(),
                default,
                default);

            store.Upsert(record);

            Assert.True(store.TryMarkDeleted("tenant", "index", "doc1"));
            Assert.False(store.TryMarkDeleted("tenant", "index", "doc1"));
            Assert.True(store.TryGet("tenant", "index", "doc1", out var stored));
            Assert.True(stored.Deleted);
        }
    }
}
