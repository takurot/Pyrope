using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Pyrope.GarnetServer.Vector
{
    public class BruteForceVectorIndex : IVectorIndex
    {
        private readonly Dictionary<string, VectorEntry> _entries = new();
        private readonly ReaderWriterLockSlim _lock = new();

        public BruteForceVectorIndex(int dimension, VectorMetric metric)
        {
            if (dimension <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(dimension), "Dimension must be positive.");
            }

            Dimension = dimension;
            Metric = metric;
        }

        public int Dimension { get; }
        public VectorMetric Metric { get; }

        public void Build()
        {
            // No-op for BruteForce
        }

        public void Snapshot(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path cannot be empty.", nameof(path));
            }

            _lock.EnterReadLock();
            try
            {
                var dto = new Dictionary<string, VectorEntryDto>(_entries.Count);
                foreach (var kvp in _entries)
                {
                    dto[kvp.Key] = new VectorEntryDto { Vector = kvp.Value.Vector, Norm = kvp.Value.Norm };
                }
                var json = System.Text.Json.JsonSerializer.Serialize(dto);
                System.IO.File.WriteAllText(path, json);
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public void Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path cannot be empty.", nameof(path));
            }

            if (!System.IO.File.Exists(path))
            {
                throw new System.IO.FileNotFoundException("Snapshot file not found.", path);
            }

            var json = System.IO.File.ReadAllText(path);
            var dto = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, VectorEntryDto>>(json);

            if (dto != null)
            {
                _lock.EnterWriteLock();
                try
                {
                    _entries.Clear();
                    foreach (var kvp in dto)
                    {
                        _entries.Add(kvp.Key, new VectorEntry(kvp.Value.Vector, kvp.Value.Norm));
                    }
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
        }

        public IndexStats GetStats()
        {
            _lock.EnterReadLock();
            try
            {
                return new IndexStats(_entries.Count, Dimension, Metric.ToString());
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public void Add(string id, float[] vector)
        {
            ValidateId(id);
            ValidateVector(vector);

            _lock.EnterWriteLock();
            try
            {
                if (_entries.ContainsKey(id))
                {
                    throw new InvalidOperationException($"Vector with id '{id}' already exists.");
                }

                _entries[id] = CreateEntry(vector);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public void Upsert(string id, float[] vector)
        {
            ValidateId(id);
            ValidateVector(vector);

            _lock.EnterWriteLock();
            try
            {
                _entries[id] = CreateEntry(vector);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public bool Delete(string id)
        {
            ValidateId(id);

            _lock.EnterWriteLock();
            try
            {
                return _entries.Remove(id);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public IEnumerable<KeyValuePair<string, float[]>> Scan()
        {
            _lock.EnterReadLock();
            try
            {
                // Return copy or direct reference? 
                // Creating new KVPs to be safe from modification during iteration if lock is released,
                // but caller should handle concurrency or we yield inside lock?
                // Yielding inside ReadLock is risky if caller delays.
                // Snapshotting list is safer.
                return _entries.Select(kvp => new KeyValuePair<string, float[]>(kvp.Key, kvp.Value.Vector)).ToList();
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public IReadOnlyList<SearchResult> Search(float[] query, int topK, SearchOptions? options = null)
        {
            ValidateVector(query);
            if (topK <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(topK), "topK must be positive.");
            }

            var maxScans = options?.MaxScans;
            if (maxScans.HasValue && maxScans.Value <= 0)
            {
                return Array.Empty<SearchResult>();
            }

            _lock.EnterReadLock();
            try
            {
                if (_entries.Count == 0)
                {
                    return Array.Empty<SearchResult>();
                }

                var scanLimit = maxScans.HasValue ? Math.Min(maxScans.Value, _entries.Count) : _entries.Count;
                if (scanLimit <= 0)
                {
                    return Array.Empty<SearchResult>();
                }

                // Keep topK results in a min-heap: O(N log K)
                var heap = new PriorityQueue<SearchResult, float>();
                var scanned = 0;

                float queryNorm = Metric == VectorMetric.Cosine ? VectorMath.ComputeNorm(query) : 0f;

                foreach (var entry in _entries)
                {
                    if (scanned >= scanLimit)
                    {
                        break;
                    }
                    scanned++;

                    var score = ComputeScore(query, entry.Value, queryNorm);
                    heap.Enqueue(new SearchResult(entry.Key, score), score);
                    if (heap.Count > topK)
                    {
                        heap.Dequeue(); // remove smallest score
                    }
                }

                if (heap.Count == 0)
                {
                    return Array.Empty<SearchResult>();
                }

                var results = new List<SearchResult>(heap.Count);
                while (heap.Count > 0)
                {
                    results.Add(heap.Dequeue());
                }
                results.Sort((a, b) => b.Score.CompareTo(a.Score));
                return results;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        private void ValidateVector(float[] vector)
        {
            if (vector == null)
            {
                throw new ArgumentNullException(nameof(vector));
            }
            if (vector.Length != Dimension)
            {
                throw new ArgumentException("Vector dimension mismatch.", nameof(vector));
            }
        }

        private static void ValidateId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Id cannot be empty.", nameof(id));
            }
        }

        private VectorEntry CreateEntry(float[] vector)
        {
            var copy = new float[vector.Length];
            Array.Copy(vector, copy, vector.Length);

            var norm = Metric == VectorMetric.Cosine ? VectorMath.ComputeNorm(copy) : 0f;
            return new VectorEntry(copy, norm);
        }

        private float ComputeScore(float[] query, VectorEntry entry, float queryNorm)
        {
            return Metric switch
            {
                VectorMetric.L2 => -VectorMath.L2Squared(query, entry.Vector),
                VectorMetric.InnerProduct => VectorMath.DotProduct(query, entry.Vector),
                VectorMetric.Cosine => VectorMath.Cosine(query, entry.Vector, queryNorm, entry.Norm),
                _ => throw new InvalidOperationException("Unsupported metric.")
            };
        }



        private sealed record VectorEntry(float[] Vector, float Norm);

        // DTO for JSON serialization (VectorEntry is private)
        private class VectorEntryDto
        {
            public float[] Vector { get; set; } = Array.Empty<float>();
            public float Norm { get; set; }
        }
    }
}
