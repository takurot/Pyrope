using System;
using System.Collections.Generic;

namespace Pyrope.GarnetServer.Model
{
    public sealed record VectorRecord
    {
        public VectorRecord(
            string tenantId,
            string indexName,
            string id,
            float[] vector,
            string? metaJson,
            IReadOnlyList<string> tags,
            IReadOnlyDictionary<string, double> numericFields,
            DateTimeOffset createdAt,
            DateTimeOffset updatedAt,
            bool deleted = false)
        {
            TenantId = tenantId ?? throw new ArgumentNullException(nameof(tenantId));
            IndexName = indexName ?? throw new ArgumentNullException(nameof(indexName));
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Vector = vector ?? throw new ArgumentNullException(nameof(vector));
            MetaJson = metaJson;
            Tags = tags ?? Array.Empty<string>();
            NumericFields = numericFields ?? new Dictionary<string, double>();
            CreatedAt = createdAt;
            UpdatedAt = updatedAt;
            Deleted = deleted;
        }

        public string TenantId { get; init; }
        public string IndexName { get; init; }
        public string Id { get; init; }
        public float[] Vector { get; init; }
        public string? MetaJson { get; init; }
        public IReadOnlyList<string> Tags { get; init; }
        public IReadOnlyDictionary<string, double> NumericFields { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
        public bool Deleted { get; init; }
    }
}
