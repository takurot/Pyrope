using System;
using System.Collections.Generic;

namespace Pyrope.GarnetServer.Vector
{
    public class IvfPqVectorIndex : IVectorIndex
    {
        public int Dimension { get; }
        public VectorMetric Metric { get; }
        public int M { get; }
        public int K { get; }
        public int NList { get; }

        private ProductQuantizer _pq;

        private readonly System.Threading.ReaderWriterLockSlim _lock = new();

        // Buffer: ID -> Vector (Raw)
        private readonly Dictionary<string, float[]> _buffer = new();

        // Inverted Index: ClusterID -> List of (ID, PQCode)
        private Dictionary<int, List<PqEntry>> _invertedLists = new();
        private List<float[]> _centroids = new();
        private List<float> _centroidNorms = new();
        private bool _isBuilt = false;

        public IvfPqVectorIndex(int dimension, VectorMetric metric, int m, int k, int nList)
        {
            Dimension = dimension;
            Metric = metric;
            M = m;
            K = k;
            NList = nList;
            _pq = new ProductQuantizer(dimension, m, k);
        }

        public void Add(string id, float[] vector)
        {
            _lock.EnterWriteLock();
            try
            {
                _buffer[id] = vector;
            }
            finally { _lock.ExitWriteLock(); }
        }

        public void Upsert(string id, float[] vector) { Add(id, vector); }
        public bool Delete(string id)
        {
            _lock.EnterWriteLock();
            try { return _buffer.Remove(id); /* TODO: logical delete from index */ }
            finally { _lock.ExitWriteLock(); }
        }

        public void Build()
        {
            _lock.EnterWriteLock();
            try
            {
                if (_buffer.Count == 0 && !_isBuilt) return;

                // 1. Gather all vectors (from buffer only for now, assuming rebuild checks buffer)
                // In production, we merge buffer + existing index data if re-building.
                var allVectors = _buffer.Values.ToList();
                if (allVectors.Count == 0) return;

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
                for (int i = 0; i < nClusters; i++) _invertedLists[i] = new List<PqEntry>();

                // Parallel encoding?
                // Just sequential for list population safety or use concurrent bags then merge.
                // Or lock per list.
                // Simple sequential for MVP Build step.
                int idx = 0;
                foreach (var kvp in _buffer)
                {
                    int cIdx = assignments[idx];
                    var res = residuals[idx];
                    var code = _pq.Encode(res);
                    _invertedLists[cIdx].Add(new PqEntry(kvp.Key, code));
                    idx++;
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
            _lock.EnterReadLock();
            try
            {
                var heap = new PriorityQueue<SearchResult, float>();
                var seen = new HashSet<string>();
                int nProbe = options?.NProbe ?? 1; // Default nProbe=1

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
                        // Coarse search uses Raw Query vs Centroid
                        float s = ComputeScore(query, _centroids[i], queryNorm, _centroidNorms[i]);
                        centroidScores.Add((i, s));
                    }
                    centroidScores.Sort((a, b) => b.Score.CompareTo(a.Score)); // Descending

                    int probes = Math.Min(nProbe, centroidScores.Count);

                    for (int i = 0; i < probes; i++)
                    {
                        int cIdx = centroidScores[i].Index;
                        if (!_invertedLists.ContainsKey(cIdx)) continue;
                        var list = _invertedLists[cIdx];
                        if (list.Count == 0) continue;

                        // ADC Preparation
                        // residualQuery = query - centroid
                        var centroid = _centroids[cIdx];
                        var resQuery = new float[Dimension];
                        for (int d = 0; d < Dimension; d++) resQuery[d] = query[d] - centroid[d];

                        // Compute Distance Table for this cluster
                        var table = _pq.ComputeDistanceTable(resQuery);

                        foreach (var entry in list)
                        {
                            if (seen.Contains(entry.Id)) continue;

                            // ADC Lookup
                            // Dist approx = Sum table[m][code[m]]
                            // This is specialized for L2-on-residuals.
                            // Wait: if Metric is Cosine, PQ on residuals is tricky.
                            // Usually PQ for Cosine simplifies to L2 on normalized vectors, but residuals break normalization.
                            // Standard approach: Use L2 on residuals, it approximates L2 distance between v and q.
                            // For Cosine, we rank by L2 distance (which correlates with Cosine for normalized vectors)
                            // OR we do inner product logic.
                            // Given we trained PQ with L2, let's use L2 distance as the proxy score.

                            float distSq = 0f;
                            for (int m = 0; m < M; m++)
                            {
                                distSq += table[m][entry.Code[m]];
                            }

                            // distSq is standard L2 squared.
                            // We need to convert to 'Score' (Higher is better).
                            // If Metric is L2: score = -distSq
                            // If Metric is Cosine: 1 - distSq/2 (if normalized).
                            // We use -distSq as generic approximation for ranking.

                            float score = -distSq;

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

        private sealed record PqEntry(string Id, byte[] Code);

        public void Snapshot(string path) { }
        public void Load(string path) { }
        public IndexStats GetStats() { return new IndexStats(0, Dimension, Metric.ToString()); }
    }
}
