using System;
using Pyrope.GarnetServer.Vector;

namespace Pyrope.GarnetServer.Vector
{
    public static class CostCalculator
    {
        /// <summary>
        /// Estimates the relative cost of a vector search operation.
        /// This is a heuristic "Proxy Cost Metric" used to adjust cache thresholds.
        /// Scaling factors:
        /// - Count: Linear (Brute Force) or Logarithmic (HNSW/IVF). Assuming Brute Force for now as worst case.
        /// - Dimension: Linear.
        /// </summary>
        public static float EstimateSearchCost(IndexStats stats, int topK = 10)
        {
            if (stats == null) return 0f;

            // Normalize Count: 10,000 vectors = 1.0 unit
            float countFactor = stats.Count / 10000f;

            // Normalize Dimension: 128 dim = 1.0 unit
            float dimFactor = stats.Dimension / 128f;
            if (dimFactor <= 0) dimFactor = 1f;

            // Base Cost = Count * Dim
            // Example: 10k vectors, 128 dim => 1.0
            // Example: 1M vectors, 1536 dim (OpenAI) => 100 * 12 => 1200.0
            float baseCost = countFactor * dimFactor;

            return baseCost;
        }
    }
}
