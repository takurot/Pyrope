using System;
using System.Collections.Generic;
using System.Linq;

namespace Pyrope.GarnetServer.Vector
{
    public class IvfPqVectorIndex : IVectorIndex, IDisposable
    {
        public int Dimension { get; }
        public VectorMetric Metric { get; }
        public int M { get; }
        public int K { get; }
        public int NList { get; }

        private ProductQuantizer _pq;

        private readonly System.Threading.ReaderWriterLockSlim _lock = new();
        private bool _disposed = false;

        // Buffer: ID -> Vector (Raw)
        private readonly Dictionary<string, float[]> _buffer = new();

        // Canonical raw vectors for re-training / rebuild continuity and deletion tracking
        private readonly Dictionary<string, float[]> _rawVectors = new();

        // Inverted Index: ClusterID -> List of (ID, PQCode, Norm)
        private Dictionary<int, List<PqEntry>> _invertedLists = new();
        private List<float[]> _centroids = new();
        private List<float> _centroidNorms = new();
        private bool _isBuilt = false;

        public IvfPqVectorIndex(int dimension, VectorMetric metric, int m, int k, int nList)
        {
            if (dimension <= 0) throw new ArgumentOutOfRangeException(nameof(dimension), "Dimension must be positive");
            if (m <= 0) throw new ArgumentOutOfRangeException(nameof(m), "M must be positive");
            if (k <= 0) throw new ArgumentOutOfRangeException(nameof(k), "K must be positive");
            if (nList <= 0) throw new ArgumentOutOfRangeException(nameof(nList), "NList must be positive");

            Dimension = dimension;
            Metric = metric;
            M = m;
            K = k;
            NList = nList;
            _pq = new ProductQuantizer(dimension, m, k);
        }

        private void ValidateId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Vector ID cannot be null or whitespace", nameof(id));
        }

        private void ValidateVector(float[] vector)
        {
            if (vector == null)
                throw new ArgumentNullException(nameof(vector));
            if (vector.Length != Dimension)
                throw new ArgumentException($"Vector dimension mismatch: expected {Dimension}, got {vector.Length}", nameof(vector));
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(IvfPqVectorIndex));
        }

        public void Add(string id, float[] vector)
        {
            ValidateId(id);
            ValidateVector(vector);

            _lock.EnterWriteLock();
            try
            {
                ThrowIfDisposed();
                _buffer[id] = vector;
                _rawVectors[id] = vector;
            }
            finally { _lock.ExitWriteLock(); }
        }

        public void Upsert(string id, float[] vector) { Add(id, vector); }

        public bool Delete(string id)
        {
            ValidateId(id);

            _lock.EnterWriteLock();
            try
            {
                ThrowIfDisposed();
                bool removed = _buffer.Remove(id);
                removed |= _rawVectors.Remove(id);

                if (_isBuilt)
                {
                    foreach (var list in _invertedLists.Values)
                    {
                        int count = list.RemoveAll(entry => entry.Id == id);
                        if (count > 0) removed = true;
                    }
                }
                return removed;
            }
            finally { _lock.ExitWriteLock(); }
        }

        public void Build()
        {
            _lock.EnterWriteLock();
            try
            {
                ThrowIfDisposed();

                if (_rawVectors.Count == 0)
                {
                    _invertedLists.Clear();
                    _centroids.Clear();
                    _centroidNorms.Clear();
                    _buffer.Clear();
                    _isBuilt = false;
                    return;
                }

                // 1. Snapshot items as stable list to guarantee 1-to-1 correspondence
                var items = _rawVectors.ToList();
                var allVectors = items.Select(x => x.Value).ToList();

                // 2. Train Coarse (IVF)
                int nClusters = Math.Min(NList, allVectors.Count);
                _centroids = KMeansUtils.Train(allVectors, nClusters, Dimension, Metric, seed: 123);
                _centroidNorms = _centroids.Select(c => Metric == VectorMetric.Cosine ? VectorMath.ComputeNorm(c) : 0f).ToList();

                // 3. Compute Residuals
                var residuals = new float[allVectors.Count][];
                var assignments = new int[allVectors.Count];

                System.Threading.Tasks.Parallel.For(0, allVectors.Count, i =>
                {
                    var vec = allVectors[i];
                    int cIdx = KMeansUtils.FindNearestCentroid(vec, _centroids, _centroidNorms, Metric);
                    assignments[i] = cIdx;

                    var res = new float[Dimension];
                    var c = _centroids[cIdx];
                    for (int d = 0; d < Dimension; d++) res[d] = vec[d] - c[d]; // Residual = v - c
                    residuals[i] = res;
                });

                // 4. Train PQ on Residuals
                _pq.Train(residuals);

                // 5. Encode and Populate Lists
                _invertedLists = new Dictionary<int, List<PqEntry>>();
                for (int i = 0; i < _centroids.Count; i++) _invertedLists[i] = new List<PqEntry>();

                for (int idx = 0; idx < items.Count; idx++)
                {
                    int cIdx = assignments[idx];
                    var res = residuals[idx];
                    var code = _pq.Encode(res);
                    float norm = Metric == VectorMetric.Cosine ? VectorMath.ComputeNorm(items[idx].Value) : 0f;
                    _invertedLists[cIdx].Add(new PqEntry(items[idx].Key, code, norm));
                }

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
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (query.Length != Dimension) throw new ArgumentException($"Query dimension mismatch: expected {Dimension}, got {query.Length}", nameof(query));
            if (topK <= 0) throw new ArgumentOutOfRangeException(nameof(topK), "topK must be greater than 0");

            _lock.EnterReadLock();
            try
            {
                ThrowIfDisposed();

                var heap = new PriorityQueue<SearchResult, float>();
                var seen = new HashSet<string>();
                int nProbe = options?.NProbe ?? 1;

                float queryNorm = Metric == VectorMetric.Cosine ? VectorMath.ComputeNorm(query) : 0f;

                // 1. Search Buffer (Exact)
                foreach (var kvp in _buffer)
                {
                    float score = ComputeScore(query, kvp.Value, queryNorm);
                    heap.Enqueue(new SearchResult(kvp.Key, score), score);
                    seen.Add(kvp.Key);
                    if (heap.Count > topK) heap.Dequeue();
                }

                if (_isBuilt)
                {
                    // 2. Find nearest centroids
                    var centroidScores = new List<(int Index, float Score)>();
                    for (int i = 0; i < _centroids.Count; i++)
                    {
                        float s = ComputeScore(query, _centroids[i], queryNorm, _centroidNorms[i]);
                        centroidScores.Add((i, s));
                    }
                    centroidScores.Sort((a, b) => b.Score.CompareTo(a.Score)); // Descending

                    int probes = Math.Min(nProbe, centroidScores.Count);

                    // Hoist inner product table precomputation outside cluster loop
                    float[][]? ipTable = (Metric == VectorMetric.InnerProduct || Metric == VectorMetric.Cosine)
                        ? _pq.ComputeInnerProductTable(query)
                        : null;

                    for (int i = 0; i < probes; i++)
                    {
                        int cIdx = centroidScores[i].Index;
                        if (!_invertedLists.ContainsKey(cIdx)) continue;
                        var list = _invertedLists[cIdx];
                        if (list.Count == 0) continue;

                        var centroid = _centroids[cIdx];

                        // Hoist coarse dot product calculation once per probed cluster
                        float coarseDot = (Metric == VectorMetric.InnerProduct || Metric == VectorMetric.Cosine)
                            ? VectorMath.DotProduct(query, centroid)
                            : 0f;

                        float[][]? l2Table = null;
                        if (Metric == VectorMetric.L2)
                        {
                            var resQuery = new float[Dimension];
                            for (int d = 0; d < Dimension; d++) resQuery[d] = query[d] - centroid[d];
                            l2Table = _pq.ComputeDistanceTable(resQuery);
                        }

                        foreach (var entry in list)
                        {
                            if (seen.Contains(entry.Id)) continue;

                            float score;
                            if (Metric == VectorMetric.L2)
                            {
                                float distSq = 0f;
                                for (int m = 0; m < M; m++)
                                {
                                    distSq += l2Table![m][entry.Code[m]];
                                }
                                score = -distSq;
                            }
                            else if (Metric == VectorMetric.InnerProduct)
                            {
                                float dotRes = 0f;
                                for (int m = 0; m < M; m++)
                                {
                                    dotRes += ipTable![m][entry.Code[m]];
                                }
                                score = coarseDot + dotRes;
                            }
                            else // Cosine
                            {
                                float dotRes = 0f;
                                for (int m = 0; m < M; m++)
                                {
                                    dotRes += ipTable![m][entry.Code[m]];
                                }
                                float dot = coarseDot + dotRes;
                                if (queryNorm < 1e-6f || entry.Norm < 1e-6f)
                                {
                                    score = 0f;
                                }
                                else
                                {
                                    score = Math.Clamp(dot / (queryNorm * entry.Norm), -1.0f, 1.0f);
                                }
                            }

                            heap.Enqueue(new SearchResult(entry.Id, score), score);
                            if (heap.Count > topK) heap.Dequeue();
                        }
                    }
                }

                var results = new List<SearchResult>();
                while (heap.Count > 0) results.Add(heap.Dequeue());
                results.Sort((a, b) => b.Score.CompareTo(a.Score));
                return results;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        private float ComputeScore(float[] q, float[] v, float qNorm, float vNorm = -1f)
        {
            if (vNorm < 0) vNorm = Metric == VectorMetric.Cosine ? VectorMath.ComputeNorm(v) : 0f;
            return Metric switch
            {
                VectorMetric.L2 => -VectorMath.L2Squared(q, v),
                VectorMetric.InnerProduct => VectorMath.DotProduct(q, v),
                VectorMetric.Cosine => VectorMath.Cosine(q, v, qNorm, vNorm),
                _ => 0f
            };
        }

        private sealed record PqEntry(string Id, byte[] Code, float Norm);

        public void Snapshot(string path) { }
        public void Load(string path) { }

        public IndexStats GetStats()
        {
            _lock.EnterReadLock();
            try
            {
                ThrowIfDisposed();
                return new IndexStats(_rawVectors.Count, Dimension, Metric.ToString());
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public void Dispose()
        {
            _lock.EnterWriteLock();
            try
            {
                _disposed = true;
            }
            finally
            {
                _lock.ExitWriteLock();
                _lock.Dispose();
            }
        }
    }
}
