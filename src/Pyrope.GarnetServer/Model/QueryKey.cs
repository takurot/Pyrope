using System;
using System.Collections.Generic;
using System.Linq;
using Pyrope.GarnetServer.Utils;
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

        public long? SimHash { get; }
        public int? ClusterId { get; }

        public QueryKey(
            string tenantId,
            string indexName,
            float[] vector,
            int topK,
            VectorMetric metric,
            IReadOnlyList<string>? filterTags,
            long? simHash = null,
            int? clusterId = null)
        {
            TenantNamespace.ValidateTenantId(tenantId);
            TenantNamespace.ValidateIndexName(indexName);
            TenantId = tenantId;
            IndexName = indexName;
            Vector = vector;
            TopK = topK;
            Metric = metric;
            SimHash = simHash;
            ClusterId = clusterId;

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

        public static int RoundK(int k)
        {
            if (k <= 5) return 5;
            if (k <= 10) return 10;
            if (k <= 20) return 20;
            if (k <= 50) return 50;
            if (k <= 100) return 100;
            return k;
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

            // L2: Check ClusterId
            if (ClusterId.HasValue && other.ClusterId.HasValue)
            {
                return ClusterId.Value == other.ClusterId.Value;
            }
            if (ClusterId.HasValue != other.ClusterId.HasValue) return false;

            // L1: Check SimHash
            if (SimHash.HasValue && other.SimHash.HasValue)
            {
                return SimHash.Value == other.SimHash.Value;
            }
            if (SimHash.HasValue != other.SimHash.HasValue) return false;

            // L0: Check vector (Exact match)
            if (Vector.Length != other.Vector.Length) return false;
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

            if (ClusterId.HasValue)
            {
                hashCode.Add(ClusterId.Value);
                // If ClusterId is present, we ignore Vector/SimHash for hashcode to match Equals logic?
                // Actually Equals logic separates them.
                // If ClusterId matches, we are equal.
                // So if ClusterId is present, HashCode should rely on it.
            }
            else if (SimHash.HasValue)
            {
                hashCode.Add(SimHash.Value);
            }
            else
            {
                // Vector: Order-dependent
                for (int i = 0; i < Vector.Length; i++)
                {
                    hashCode.Add(Vector[i]);
                }
            }

            return hashCode.ToHashCode();
        }
    }
}
