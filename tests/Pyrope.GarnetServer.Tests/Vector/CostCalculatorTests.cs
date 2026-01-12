using System;
using Xunit;
using Pyrope.GarnetServer.Vector;

namespace Pyrope.GarnetServer.Tests.Vector
{
    public class CostCalculatorTests
    {
        [Fact]
        public void EstimateSearchCost_ScalesLinearlyWithCount()
        {
            // 10k vectors, 128 dim => 1.0 logic
            var baseStats = new IndexStats(10000, 128, "L2");
            var costBase = CostCalculator.EstimateSearchCost(baseStats);

            var largeStats = new IndexStats(20000, 128, "L2");
            var costLarge = CostCalculator.EstimateSearchCost(largeStats);

            Assert.Equal(costBase * 2, costLarge, 0.001f);
        }

        [Fact]
        public void EstimateSearchCost_ScalesLinearlyWithDimension()
        {
            var baseStats = new IndexStats(10000, 128, "L2");
            var costBase = CostCalculator.EstimateSearchCost(baseStats);

            var highDimStats = new IndexStats(10000, 256, "L2");
            var costHighDim = CostCalculator.EstimateSearchCost(highDimStats);

            Assert.Equal(costBase * 2, costHighDim, 0.001f);
        }

        [Fact]
        public void EstimateSearchCost_ZeroStats_ReturnsZero()
        {
            var cost = CostCalculator.EstimateSearchCost(null!);
            Assert.Equal(0f, cost);
        }

        [Fact]
        public void EstimateSearchCost_OpenAIModelExample()
        {
            // 1M vectors, 1536 dim
            var stats = new IndexStats(1_000_000, 1536, "Cosine");
            var cost = CostCalculator.EstimateSearchCost(stats);

            // Count Factor = 1000000 / 10000 = 100.
            // Dim Factor = 1536 / 128 = 12.
            // Expected = 1200.
            Assert.Equal(1200f, cost, 1.0f);
        }
    }
}
