using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Text.Json;

namespace Pyrope.GarnetServer.Vector
{
    public class IvfFlatVectorIndex : IVectorIndex
    {
        private readonly ReaderWriterLockSlim _lock = new();

        // Configuration
        private readonly int _nList; // Target number of clusters
        public int CombineNProbe { get; set; } = 3; // How many clusters to search

        // State - Pre-Build (Buffer)
        private readonly Dictionary<string, VectorEntry> _buffer = new();

        // State - Post-Build (Index)
        private bool _isBuilt = false;
        private List<float[]> _centroids = new();
        private Dictionary<int, List<KeyValuePair<string, VectorEntry>>> _invertedLists = new();

        // Cached Centroid Norms (Optimization for Cosine)
        private List<float> _centroidNorms = new();

        public IvfFlatVectorIndex(int dimension, VectorMetric metric, int nList = 100)
        {
            if (dimension <= 0) throw new ArgumentOutOfRangeException(nameof(dimension));
            Dimension = dimension;
            Metric = metric;
            _nList = nList;
        }

        public int Dimension { get; }
        public VectorMetric Metric { get; }

        public void Add(string id, float[] vector)
        {
            ValidateId(id);
            ValidateVector(vector);

            _lock.EnterWriteLock();
            try
            {
                var entry = CreateEntry(vector);
                _buffer[id] = entry;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public void Upsert(string id, float[] vector)
        {
            Add(id, vector);
        }

        public bool Delete(string id)
        {
            ValidateId(id);
            _lock.EnterWriteLock();
            try
            {
                bool mostlyremoved = _buffer.Remove(id);
                if (_isBuilt)
                {
                    // Slow path: find and remove
                    foreach (var list in _invertedLists.Values)
                    {
                        int rm = list.RemoveAll(x => x.Key == id);
                        if (rm > 0) mostlyremoved = true;
                    }
                }
                return mostlyremoved;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public void Build()
        {
            _lock.EnterWriteLock();
            try
            {
                // 1. Gather all vectors (Deduplicating: Buffer wins)
                var uniqueData = new Dictionary<string, VectorEntry>();

                // First add existing index data
                if (_isBuilt)
                {
                    foreach (var list in _invertedLists.Values)
                    {
                        foreach (var item in list)
                        {
                            uniqueData[item.Key] = item.Value;
                        }
                    }
                }

                // Then overwrite/add buffer data
                foreach (var kvp in _buffer)
                {
                    uniqueData[kvp.Key] = kvp.Value;
                }

                if (uniqueData.Count == 0) return;

                var allVectors = uniqueData.Select(x => x.Value.Vector).ToList();

                // 2. Train KMeans
                int k = Math.Min(_nList, uniqueData.Count);
                if (k <= 0) k = 1;

                var centroids = TrainKMeans(allVectors, k);

                // Precompile centroid norms for faster assignment/search if needed
                var cNorms = centroids.Select(c => Metric == VectorMetric.Cosine ? VectorMath.ComputeNorm(c) : 0f).ToList();

                // 3. Assign
                var newLists = new Dictionary<int, List<KeyValuePair<string, VectorEntry>>>();
                for (int i = 0; i < k; i++) newLists[i] = new List<KeyValuePair<string, VectorEntry>>();

                foreach (var item in uniqueData)
                {
                    int bestC = FindNearestCentroid(item.Value.Vector, centroids, cNorms);
                    newLists[bestC].Add(item);
                }

                // 4. Commit
                _centroids = centroids;
                _centroidNorms = cNorms;
                _invertedLists = newLists;
                _buffer.Clear();
                _isBuilt = true;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public IReadOnlyList<SearchResult> Search(float[] query, int topK, SearchOptions? options = null)
        {
            ValidateVector(query);

            int nProbe = CombineNProbe;
            int maxScans = int.MaxValue;

            if (options != null)
            {
                if (options.MaxScans.HasValue) maxScans = options.MaxScans.Value;
                if (options.NProbe.HasValue) nProbe = options.NProbe.Value;
            }

            _lock.EnterReadLock();
            try
            {
                var heap = new PriorityQueue<SearchResult, float>();
                var seenIds = new HashSet<string>();
                int scanned = 0;

                float queryNorm = Metric == VectorMetric.Cosine ? VectorMath.ComputeNorm(query) : 0f;

                // 1. Search Buffer (Exact)
                foreach (var kvp in _buffer)
                {
                    if (scanned >= maxScans) break;
                    scanned++;

                    float score = ComputeScore(query, kvp.Value, queryNorm);
                    heap.Enqueue(new SearchResult(kvp.Key, score), score);
                    seenIds.Add(kvp.Key);

                    if (heap.Count > topK) heap.Dequeue();
                }

                // 2. Search Index (Approx)
                if (_isBuilt && _centroids.Count > 0 && scanned < maxScans)
                {
                    // Find nearest nProbe centroids
                    var centroidScores = new List<(int Index, float Score)>();
                    for (int i = 0; i < _centroids.Count; i++)
                    {
                        // Use precomputed norm for centroids
                        var cEntry = new VectorEntry(_centroids[i], _centroidNorms[i]);
                        float s = ComputeScore(query, cEntry, queryNorm);
                        centroidScores.Add((i, s));
                    }

                    // Sort centroids by score (Descending: Higher is better)
                    centroidScores.Sort((a, b) => b.Score.CompareTo(a.Score));

                    int probes = Math.Min(nProbe, centroidScores.Count);

                    for (int i = 0; i < probes; i++)
                    {
                        if (scanned >= maxScans) break;

                        int cIdx = centroidScores[i].Index;
                        if (_invertedLists.TryGetValue(cIdx, out var list))
                        {
                            foreach (var item in list)
                            {
                                if (scanned >= maxScans) break;
                                if (seenIds.Contains(item.Key)) continue; // Already found in buffer

                                scanned++;
                                float score = ComputeScore(query, item.Value, queryNorm);
                                heap.Enqueue(new SearchResult(item.Key, score), score);
                                if (heap.Count > topK) heap.Dequeue();
                            }
                        }
                    }
                }

                // Unload Heap
                var results = new List<SearchResult>(heap.Count);
                while (heap.Count > 0) results.Add(heap.Dequeue());
                results.Sort((a, b) => b.Score.CompareTo(a.Score));
                return results;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public void Snapshot(string path)
        {
            _lock.EnterReadLock();
            try
            {
                var state = new IvfStateDto
                {
                    Dimension = Dimension,
                    Metric = Metric,
                    IsBuilt = _isBuilt,
                    Centroids = _centroids,
                    CentroidNorms = _centroidNorms,
                    Buffer = _buffer.ToDictionary(k => k.Key, v => new VectorEntryDto { Vector = v.Value.Vector, Norm = v.Value.Norm }),
                    InvertedLists = _invertedLists.ToDictionary(
                        k => k.Key.ToString(),
                        v => v.Value.Select(x => new KeyedVectorDto { Id = x.Key, Vector = x.Value.Vector, Norm = x.Value.Norm }).ToList()
                    )
                };

                var json = JsonSerializer.Serialize(state);
                System.IO.File.WriteAllText(path, json);
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public void Load(string path)
        {
            if (!System.IO.File.Exists(path)) throw new System.IO.FileNotFoundException("Snapshot not found", path);
            var json = System.IO.File.ReadAllText(path);
            var state = JsonSerializer.Deserialize<IvfStateDto>(json);

            if (state == null) return;

            _lock.EnterWriteLock();
            try
            {
                _isBuilt = state.IsBuilt;
                _centroids = state.Centroids ?? new List<float[]>();
                _centroidNorms = state.CentroidNorms ?? new List<float>();

                _buffer.Clear();
                if (state.Buffer != null)
                {
                    foreach (var kvp in state.Buffer)
                        _buffer[kvp.Key] = new VectorEntry(kvp.Value.Vector, kvp.Value.Norm);
                }

                _invertedLists.Clear();
                if (state.InvertedLists != null)
                {
                    foreach (var kvp in state.InvertedLists)
                    {
                        int cIdx = int.Parse(kvp.Key);
                        var list = kvp.Value.Select(x => new KeyValuePair<string, VectorEntry>(x.Id, new VectorEntry(x.Vector, x.Norm))).ToList();
                        _invertedLists[cIdx] = list;
                    }
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public IndexStats GetStats()
        {
            _lock.EnterReadLock();
            try
            {
                int count = _buffer.Count + _invertedLists.Values.Sum(x => x.Count);
                return new IndexStats(count, Dimension, Metric.ToString());
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }


        // --- Internals ---

        private List<float[]> TrainKMeans(List<float[]> data, int k)
        {
            var rnd = new Random(42);
            var centroids = data.OrderBy(_ => rnd.Next()).Take(k).Select(x => (float[])x.Clone()).ToList();

            if (centroids.Count < k)
            {
                // Edge case: fewer data points than k
                // Just keep what we have
            }

            int maxIter = 10;
            // Precompute L2 norms needed? 
            // Standard KMeans uses Euclidean distance (L2) regardless of target metric usually, 
            // but for Cosine similarity search, Spherical K-Means is better.
            // For MVP, we stick to standard KMeans (L2 minimizes variance).

            for (int iter = 0; iter < maxIter; iter++)
            {
                var clusters = new List<float[]>[k];
                for (int i = 0; i < k; i++) clusters[i] = new List<float[]>();

                bool changed = false;

                // Temp norms for centroids during training iteration
                var cNorms = centroids.Select(c => Metric == VectorMetric.Cosine ? VectorMath.ComputeNorm(c) : 0f).ToList();

                // Assign
                foreach (var vec in data)
                {
                    int best = FindNearestCentroid(vec, centroids, cNorms);
                    clusters[best].Add(vec);
                }

                // Update
                for (int i = 0; i < k; i++)
                {
                    if (clusters[i].Count == 0) continue;

                    var newC = new float[Dimension];
                    foreach (var vec in clusters[i])
                    {
                        for (int d = 0; d < Dimension; d++) newC[d] += vec[d];
                    }
                    for (int d = 0; d < Dimension; d++) newC[d] /= clusters[i].Count;

                    if (!ArraysEqual(centroids[i], newC))
                    {
                        centroids[i] = newC;
                        changed = true;
                    }
                }

                if (!changed) break;
            }
            return centroids;
        }

        private bool ArraysEqual(float[] a, float[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (Math.Abs(a[i] - b[i]) > 1e-6) return false;
            return true;
        }

        private int FindNearestCentroid(float[] vec, List<float[]> centroids, List<float> centroidNorms)
        {
            int bestIndex = 0;
            // Fix: Initialize bestDist correctly based on metric
            // ComputeScore returns a score where Higher is Better.
            // So we start with MinValue.
            float bestScore = float.MinValue;

            float vecNorm = Metric == VectorMetric.Cosine ? VectorMath.ComputeNorm(vec) : 0f;

            for (int i = 0; i < centroids.Count; i++)
            {
                // Fix: Pass centroid norm
                float score = ComputeScore(vec, new VectorEntry(centroids[i], centroidNorms[i]), vecNorm);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }
            return bestIndex;
        }

        // Helpers
        private void ValidateVector(float[] vector)
        {
            if (vector == null) throw new ArgumentNullException(nameof(vector));
            if (vector.Length != Dimension) throw new ArgumentException("Vector dimension mismatch");
        }
        private void ValidateId(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Id empty");
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
                _ => throw new InvalidOperationException()
            };
        }



        private sealed record VectorEntry(float[] Vector, float Norm);

        // DTOs
        private class IvfStateDto
        {
            public int Dimension { get; set; }
            public VectorMetric Metric { get; set; }
            public bool IsBuilt { get; set; }
            public List<float[]> Centroids { get; set; }
            public List<float> CentroidNorms { get; set; }
            public Dictionary<string, VectorEntryDto> Buffer { get; set; }
            public Dictionary<string, List<KeyedVectorDto>> InvertedLists { get; set; }
        }

        private class VectorEntryDto
        {
            public float[] Vector { get; set; }
            public float Norm { get; set; }
        }

        private class KeyedVectorDto
        {
            public string Id { get; set; }
            public float[] Vector { get; set; }
            public float Norm { get; set; }
        }
    }
}
