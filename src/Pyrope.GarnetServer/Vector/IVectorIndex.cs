using System.Collections.Generic;

namespace Pyrope.GarnetServer.Vector
{
    public enum VectorMetric
    {
        L2,
        InnerProduct,
        Cosine
    }

    public record SearchResult(string Id, float Score);

    public interface IVectorIndex
    {
        int Dimension { get; }
        VectorMetric Metric { get; }

        void Add(string id, float[] vector);
        void Upsert(string id, float[] vector);
        bool Delete(string id);
        IReadOnlyList<SearchResult> Search(float[] query, int topK);
    }
}
