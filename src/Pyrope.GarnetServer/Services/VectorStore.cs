using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Pyrope.GarnetServer.Model;

namespace Pyrope.GarnetServer.Services
{
    public sealed class VectorStore
    {
        private readonly ConcurrentDictionary<string, VectorRecord> _records = new(StringComparer.Ordinal);

        public bool TryAdd(VectorRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));

            var key = GetRecordKey(record.TenantId, record.IndexName, record.Id);
            var now = DateTimeOffset.UtcNow;
            var stored = record with { CreatedAt = now, UpdatedAt = now };
            return _records.TryAdd(key, stored);
        }

        public VectorRecord Upsert(VectorRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));

            var key = GetRecordKey(record.TenantId, record.IndexName, record.Id);
            var now = DateTimeOffset.UtcNow;
            return _records.AddOrUpdate(
                key,
                _ => record with { CreatedAt = now, UpdatedAt = now },
                (_, existing) => record with { CreatedAt = existing.CreatedAt, UpdatedAt = now });
        }

        public bool TryGet(string tenantId, string indexName, string id, out VectorRecord record)
        {
            var key = GetRecordKey(tenantId, indexName, id);
            return _records.TryGetValue(key, out record!);
        }

        public bool TryMarkDeleted(string tenantId, string indexName, string id)
        {
            var key = GetRecordKey(tenantId, indexName, id);
            if (!_records.TryGetValue(key, out var existing))
            {
                return false;
            }

            if (existing.Deleted)
            {
                return false;
            }

            var now = DateTimeOffset.UtcNow;
            var updated = existing with { Deleted = true, UpdatedAt = now };
            _records[key] = updated;
            return true;
        }

        public void Clear()
        {
            _records.Clear();
        }

        private static string GetRecordKey(string tenantId, string indexName, string id)
        {
            if (string.IsNullOrWhiteSpace(tenantId)) throw new ArgumentException("Tenant id cannot be empty.", nameof(tenantId));
            if (string.IsNullOrWhiteSpace(indexName)) throw new ArgumentException("Index name cannot be empty.", nameof(indexName));
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Id cannot be empty.", nameof(id));

            return $"{tenantId}:{indexName}:{id}";
        }
    }
}
