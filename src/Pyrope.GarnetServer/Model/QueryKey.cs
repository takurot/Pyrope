using System;
using System.Collections.Generic;
using System.Linq;
using Pyrope.GarnetServer.Vector;

namespace Pyrope.GarnetServer.Model
{
    public sealed class QueryKey : IEquatable<QueryKey>
    {
        public string TenantId { get; }
        public string IndexName { get; }
        public float[] Vector { get; }
        public int TopK { get; }
        public VectorMetric Metric { get; }
        public IReadOnlySet<string> FilterTags { get; }

        public QueryKey(
            string tenantId,
            string indexName,
            float[] vector,
            int topK,
            VectorMetric metric,
            IReadOnlyList<string>? filterTags)
        {
            TenantId = tenantId;
            IndexName = indexName;
            Vector = vector;
            TopK = topK;
            Metric = metric;
            
            // Normalize tags: Case-sensitive, sorted/set for uniqueness and order-independence in equality
            if (filterTags == null || filterTags.Count == 0)
            {
                FilterTags = new HashSet<string>();
            }
            else
            {
                 FilterTags = new HashSet<string>(filterTags);
            }
        }

        public bool Equals(QueryKey? other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;

            // Simple checks first
            if (TopK != other.TopK) return false;
            if (Metric != other.Metric) return false;
            if (!string.Equals(TenantId, other.TenantId, StringComparison.Ordinal)) return false;
            if (!string.Equals(IndexName, other.IndexName, StringComparison.Ordinal)) return false;

            // Check tags (Set equality)
            if (!FilterTags.SetEquals(other.FilterTags)) return false;

            // Check vector (Exact match)
            // Note: Floating point equality can be tricky, but for caching exact same vector input is the expectation for Level 0.
            if (Vector.Length != other.Vector.Length) return false;
            
            // Using Span sequence equal for performance if possible, or simple loop
            // ReadOnlySpan<float> v1 = Vector;
            // ReadOnlySpan<float> v2 = other.Vector;
            // return v1.SequenceEqual(v2);
            
            // Fallback for IEnumerable/Array if Span is annoying in this context (it is fine here though)
            return Vector.AsSpan().SequenceEqual(other.Vector.AsSpan());
        }

        public override bool Equals(object? obj)
        {
            return ReferenceEquals(this, obj) || obj is QueryKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            var hashCode = new HashCode();
            hashCode.Add(TenantId, StringComparer.Ordinal);
            hashCode.Add(IndexName, StringComparer.Ordinal);
            hashCode.Add(TopK);
            hashCode.Add(Metric);

            // Vector Hash
            // We can't easily hash every float in a large vector for the hash code, performance penalty.
            // But for correctness we should include at least some representative parts or a subset.
            // For a robust implementation, let's hash the length and maybe a few sample points or a stride.
            // Or just loop through all if vectors aren't huge. Let's try looping all for correctness first (Level 0).
            // Optimization: If vector is immutable, we can cache the hash code. 
            // Since it's a record-like class, let's assume it's effectively immutable.
            
            // Actually, HashCode.Add(IEnumerable) isn't standard in older .NET, but we are likely on .NET 6/8.
            // Let's implement a manual accumulation for vector and tags to be safe and order-independent for tags.
            
            // Tags: Order-independent hash. XOR is good for this.
            int tagsHash = 0;
            if (FilterTags != null)
            {
                foreach (var tag in FilterTags)
                {
                    tagsHash ^= tag.GetHashCode(StringComparison.Ordinal);
                }
            }
            hashCode.Add(tagsHash);

            // Vector: Order-dependent
            // To avoid iterating 1000s of floats every GetHashCode call, we could cache it. 
            // But let's keep it simple for now. 
            // We will just hash the first few elements and length to speed it up, assuming vectors are distinct enough.
            // BUT "Exact Match" is required. If we only hash partial, we rely on Equals to distinct. Resulting in collisions if first few dims are same.
            // Better to hash the whole thing or a strided sample.
            
            // Let's do a stride loop for performance/good distribution balance.
            for (int i = 0; i < Vector.Length; i++)
            {
                // Simple accumulation
                hashCode.Add(Vector[i]);
            }

            return hashCode.ToHashCode();
        }
    }
}
