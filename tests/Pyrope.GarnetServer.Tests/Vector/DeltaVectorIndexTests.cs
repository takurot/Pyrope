using System;
using System.Linq;
using Xunit;
using Pyrope.GarnetServer.Vector;
using System.Collections.Generic;

namespace Pyrope.GarnetServer.Tests.Vector
{
    public class DeltaVectorIndexTests
    {
        private readonly DeltaVectorIndex _deltaIndex;
        private readonly BruteForceVectorIndex _head;
        private readonly BruteForceVectorIndex _tail;

        public DeltaVectorIndexTests()
        {
            // Use BruteForce for both head and tail for testing logic
            _head = new BruteForceVectorIndex(2, VectorMetric.L2);
            _tail = new BruteForceVectorIndex(2, VectorMetric.L2);
            _deltaIndex = new DeltaVectorIndex(_head, _tail);
        }

        [Fact]
        public void Add_WritesToHead()
        {
            _deltaIndex.Add("1", new float[] { 1, 0 });

            // Check head has it
            var headRes = _head.Search(new float[] { 1, 0 }, 1);
            Assert.Single(headRes);
            Assert.Equal("1", headRes[0].Id);

            // Check tail does not
            var tailRes = _tail.Search(new float[] { 1, 0 }, 1);
            Assert.Empty(tailRes);
        }

        [Fact]
        public void Search_MergesResults()
        {
            _head.Add("head1", new float[] { 1, 0 });
            _tail.Add("tail1", new float[] { 0, 1 });

            // Search near head1
            var results = _deltaIndex.Search(new float[] { 1, 0 }, 10);

            Assert.Equal(2, results.Count);
            Assert.Contains(results, r => r.Id == "head1");
            Assert.Contains(results, r => r.Id == "tail1");
        }

        [Fact]
        public void Search_HeadOverridesTail()
        {
            // Version 1 in Tail (far away)
            _tail.Add("doc1", new float[] { 100, 100 });

            // Version 2 in Head (close)
            _head.Add("doc1", new float[] { 1, 0 });

            var results = _deltaIndex.Search(new float[] { 1, 0 }, 10);

            Assert.Single(results); // Should deduplicate by ID
            Assert.Equal("doc1", results[0].Id);
            Assert.Equal(0f, results[0].Score, 0.001f); // Should return Head score (0 distance)
        }

        [Fact]
        public void Delete_PropagatesToBoth()
        {
            _head.Add("doc1", new float[] { 1, 0 });
            _tail.Add("doc1", new float[] { 1, 0 });

            _deltaIndex.Delete("doc1");

            var res = _deltaIndex.Search(new float[] { 1, 0 }, 10);
            Assert.Empty(res);
        }
    }
}
