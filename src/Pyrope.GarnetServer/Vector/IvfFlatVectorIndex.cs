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
                // For simplicity in this implementation, everything goes to buffer first.
                // If we are already built, we could assign to cluster immediately (Online IVF),
                // but the prompt implies a batch-built Tail index. 
                // However, to support real-time updates without rebuilding constantly, 
                // we'll stick to: Updates -> Buffer.
                // Re-Build clears buffer and reclusters everything.

                // If ID exists in buffer, update it.
                // If ID exists in clusters, we should technically remove it or mark it.
                // But efficient delete in IVF is hard. 
                // Logic: "Add" overwrites. 
                // We'll add to Buffer. Search checks Buffer first. 
                // If duplicates exist in index, we might return them unless we filter.
                // For now, let's just use the Buffer as the write path.

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
                // Also need to remove from built index if present.
                // This is O(N) without an ID->Cluster map.
                // For a Tail index, deletes might be rare or batch handled.
                // We will implement a lazy scan or just "Best Effort" from buffer.
                // To do it correctly:
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
                // 1. Gather all vectors (Buffer + Existing Clusters)
                var allData = new List<KeyValuePair<string, VectorEntry>>();
                allData.AddRange(_buffer);
                if (_isBuilt)
                {
                    foreach (var list in _invertedLists.Values)
                    {
                        allData.AddRange(list);
                    }
                }

                if (allData.Count == 0) return;

                // 2. Train KMeans
                int k = Math.Min(_nList, allData.Count);
                if (k <= 0) k = 1;

                var centroids = TrainKMeans(allData.Select(x => x.Value.Vector).ToList(), k);

                // 3. Assign
                var newLists = new Dictionary<int, List<KeyValuePair<string, VectorEntry>>>();
                for (int i = 0; i < k; i++) newLists[i] = new List<KeyValuePair<string, VectorEntry>>();

                foreach (var item in allData)
                {
                    int bestC = FindNearestCentroid(item.Value.Vector, centroids);
                    newLists[bestC].Add(item);
                }

                // 4. Commit
                _centroids = centroids;
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

            int nProbe = CombineNProbe; // Default
            int maxScans = int.MaxValue;
            if (options != null)
            {
                if (options.MaxScans.HasValue) maxScans = options.MaxScans.Value;
                // Could allow option to override nProbe if we added it to SearchOptions
            }

            _lock.EnterReadLock();
            try
            {
                var heap = new PriorityQueue<SearchResult, float>();
                var seenIds = new HashSet<string>();

                // 1. Search Buffer (Exact)
                foreach (var kvp in _buffer)
                {
                    float score = ComputeScore(query, kvp.Value);
                    heap.Enqueue(new SearchResult(kvp.Key, score), score);
                    seenIds.Add(kvp.Key);

                    if (heap.Count > topK) heap.Dequeue();
                }

                // 2. Search Index (Approx)
                if (_isBuilt && _centroids.Count > 0)
                {
                    // Find nearest nProbe centroids
                    var centroidScores = new List<(int Index, float Score)>();
                    for (int i = 0; i < _centroids.Count; i++)
                    {
                        float s = ComputeScore(query, new VectorEntry(_centroids[i], 0)); // Norm not needed for centroid selection usually? Or used?
                        // Actually ComputeScore handles standard metric logic.
                        centroidScores.Add((i, s));
                    }

                    // Sort centroids by score (Descending: Higher is better)
                    centroidScores.Sort((a, b) => b.Score.CompareTo(a.Score));

                    int probes = Math.Min(nProbe, centroidScores.Count);
                    int scanned = 0;

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
                                float score = ComputeScore(query, item.Value);
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
                    // Serialize Buffer
                    Buffer = _buffer.ToDictionary(k => k.Key, v => new VectorEntryDto { Vector = v.Value.Vector, Norm = v.Value.Norm }),
                    // Serialize Inverted Lists
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
                // Validate dimension/metric match if needed
                _isBuilt = state.IsBuilt;
                _centroids = state.Centroids ?? new List<float[]>();

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
                // Pad with zeros if not enough data? Or just reduce k?
                // Logic above ensures k <= data.Count
            }

            int maxIter = 10;
            for (int iter = 0; iter < maxIter; iter++)
            {
                var clusters = new List<float[]>[k];
                for (int i = 0; i < k; i++) clusters[i] = new List<float[]>();

                bool changed = false;

                // Assign
                foreach (var vec in data)
                {
                    int best = FindNearestCentroid(vec, centroids);
                    clusters[best].Add(vec);
                }

                // Update
                for (int i = 0; i < k; i++)
                {
                    if (clusters[i].Count == 0) continue; // Keep old centroid if empty

                    var newC = new float[Dimension];
                    foreach (var vec in clusters[i])
                    {
                        for (int d = 0; d < Dimension; d++) newC[d] += vec[d];
                    }
                    for (int d = 0; d < Dimension; d++) newC[d] /= clusters[i].Count;

                    // Check change
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

        private int FindNearestCentroid(float[] vec, List<float[]> centroids)
        {
            int bestIndex = 0;
            float bestDist = Metric == VectorMetric.L2 ? float.MaxValue : float.MinValue;

            for (int i = 0; i < centroids.Count; i++)
            {
                // Centroids are just vectors, use same metric
                // For K-Means, strictly L2 is standard for centroid update logic (mean).
                // But for assignment in metric space, we should use the Metric.
                // However, averaging vectors only minimizes L2 variance. 
                // Using Cosine metric with simple averaging is Spherical K-Means roughly.
                // For this MVP, we use L2 logic or generic score logic.
                // Let's use generic score:

                // Note: VectorEntry wrapper needed for ComputeScore
                float score = ComputeScore(vec, new VectorEntry(centroids[i], 0));

                if (score > bestDist) // Higher score is better in our system
                {
                    bestDist = score;
                    bestIndex = i;
                }
            }
            return bestIndex;
        }

        // Helpers copied/adapted from BruteForce logic
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
            var norm = Metric == VectorMetric.Cosine ? ComputeNorm(copy) : 0f;
            return new VectorEntry(copy, norm);
        }

        private float ComputeScore(float[] query, VectorEntry entry)
        {
            return Metric switch
            {
                VectorMetric.L2 => -SquaredL2(query, entry.Vector),
                VectorMetric.InnerProduct => Dot(query, entry.Vector),
                VectorMetric.Cosine => Cosine(query, entry.Vector, entry.Norm),
                _ => throw new InvalidOperationException()
            };
        }

        private static float Dot(float[] a, float[] b)
        {
            var sum = 0f;
            for (var i = 0; i < a.Length; i++) sum += a[i] * b[i];
            return sum;
        }

        private static float SquaredL2(float[] a, float[] b)
        {
            var sum = 0f;
            for (var i = 0; i < a.Length; i++) { var d = a[i] - b[i]; sum += d * d; }
            return sum;
        }

        private static float ComputeNorm(float[] vector)
        {
            var sum = 0f;
            for (var i = 0; i < vector.Length; i++) sum += vector[i] * vector[i];
            return (float)Math.Sqrt(sum);
        }

        private static float Cosine(float[] query, float[] vector, float vectorNorm)
        {
            var queryNorm = ComputeNorm(query);
            if (queryNorm == 0f || vectorNorm == 0f) return 0f;
            return Dot(query, vector) / (queryNorm * vectorNorm);
        }

        private sealed record VectorEntry(float[] Vector, float Norm);

        // DTOs
        private class IvfStateDto
        {
            public int Dimension { get; set; }
            public VectorMetric Metric { get; set; }
            public bool IsBuilt { get; set; }
            public List<float[]> Centroids { get; set; }
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
