using System;
using System.Collections.Generic;
using System.Threading;

namespace Pyrope.GarnetServer.Vector
{
    public class HnswVectorIndex : IVectorIndex
    {
        private readonly ReaderWriterLockSlim _lock = new();

        // Refactor: Flat Vector Storage
        // Use List<float> for expandability, access via Span
        private readonly List<float> _flatVectors = new();

        private readonly List<Node> _nodes = new();
        private readonly Dictionary<string, int> _idMap = new();
        private int _entryPointId = -1;
        private int _maxLayer = -1;
        private bool _isBuilt = false;
#pragma warning disable CS0414
        private readonly Random _rng = new();
        private readonly double _levelLambda;

        public int Dimension { get; }
        public VectorMetric Metric { get; }
        public int M { get; }
        public int EfConstruction { get; }
        public int EfSearch { get; set; }

        public HnswVectorIndex(int dimension, VectorMetric metric, int m = 16, int efConstruction = 200, int efSearch = 10)
        {
            if (dimension <= 0) throw new ArgumentOutOfRangeException(nameof(dimension));
            if (m < 2) throw new ArgumentOutOfRangeException(nameof(m), "M must be >= 2");
            Dimension = dimension;
            Metric = metric;
            M = m;
            EfConstruction = efConstruction;
            EfSearch = efSearch;
            _levelLambda = 1.0 / Math.Log(m);
        }

        public IEnumerable<KeyValuePair<string, float[]>> Scan()
        {
            _lock.EnterReadLock();
            try
            {
                var result = new List<KeyValuePair<string, float[]>>(_nodes.Count);
                for(int i = 0; i < _nodes.Count; i++)
                {
                   if(!_nodes[i].IsDeleted)
                   {
                       var vec = GetVectorSpan(i).ToArray(); // Clone for export
                       result.Add(new KeyValuePair<string, float[]>(_nodes[i].ExternalId, vec));
                   }
                }
                return result;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public void Add(string id, float[] vector)
        {
            if (vector == null) throw new ArgumentNullException(nameof(vector));
            if (vector.Length != Dimension) throw new ArgumentException("Dimension mismatch");

            // Normalize if Cosine to ensure correctness
            // Copy to avoid modifying caller's array
            float[] vectorToAdd = vector;
            if (Metric == VectorMetric.Cosine)
            {
                float norm = VectorMath.ComputeNorm(vector);
                if (norm > 1e-6f && Math.Abs(norm - 1.0f) > 1e-4f)
                {
                    vectorToAdd = new float[Dimension];
                    for (int i = 0; i < Dimension; i++) vectorToAdd[i] = vector[i] / norm;
                }
                else if (Math.Abs(norm - 1.0f) <= 1e-4f)
                {
                     // Already close to 1, just use it
                     vectorToAdd = vector;
                }
            }

            _lock.EnterWriteLock();
            try
            {
                if (_idMap.TryGetValue(id, out int existingId))
                {
                    _nodes[existingId].IsDeleted = true;
                    _idMap.Remove(id);
                }

                int newNodeId = _nodes.Count;
                int level = GenerateRandomLevel();

                // Add split: Node (structure) + Vector (data)
                var newNode = new Node(id, level, M); // Vector removed from Node
                _nodes.Add(newNode);
                _flatVectors.AddRange(vectorToAdd); // Append flat

                _idMap[id] = newNodeId;

                int currObj = _entryPointId;

                if (currObj != -1)
                {
                    // Access via GetVectorSpan
                    float dist = Dist(GetVectorSpan(newNodeId), GetVectorSpan(currObj));

                    // 1. Search from top layer down to level+1
                    for (int l = _maxLayer; l > level; l--)
                    {
                        bool changed = true;
                        while (changed)
                        {
                            changed = false;
                            var neighbors = _nodes[currObj].GetNeighbors(l);
                            foreach (var neighborId in neighbors)
                            {
                                if (_nodes[neighborId].IsDeleted) continue;

                                float d = Dist(GetVectorSpan(newNodeId), GetVectorSpan(neighborId));
                                if (d < dist)
                                {
                                    dist = d;
                                    currObj = neighborId;
                                    changed = true;
                                }
                            }
                        }
                    }

                    // 2. Search & Link from level down to 0
                    // Cache the query vector span to avoid re-calculating offset if possible, 
                    // but since we are modifying the graph, safe to just pass Span.
                    ReadOnlySpan<float> qVec = GetVectorSpan(newNodeId);

                    for (int l = Math.Min(level, _maxLayer); l >= 0; l--)
                    {
                        var candidates = SearchLayer(currObj, qVec, EfConstruction, l);
                        var neighbors = SelectNeighbors(candidates, M, l);

                        foreach (var neighborId in neighbors)
                        {
                            newNode.AddNeighbor(l, neighborId);
                            var neighbor = _nodes[neighborId];
                            neighbor.AddNeighbor(l, newNodeId);

                            if (neighbor.GetNeighbors(l).Count > (l == 0 ? M * 2 : M))
                            {
                                PruneNeighbors(neighborId, l); // Pass ID instead of object
                            }
                        }

                        if (candidates.Count > 0)
                        {
                            currObj = candidates[0].Id;
                        }
                    }

                    if (level > _maxLayer)
                    {
                        _maxLayer = level;
                        _entryPointId = newNodeId;
                    }
                }
                else
                {
                    _entryPointId = newNodeId;
                    _maxLayer = level;
                }
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
            _lock.EnterWriteLock();
            try
            {
                if (_idMap.TryGetValue(id, out int nodeId))
                {
                    _nodes[nodeId].IsDeleted = true;
                    _idMap.Remove(id);
                    return true;
                }
                return false;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public IReadOnlyList<SearchResult> Search(float[] query, int topK, SearchOptions? options = null)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (query.Length != Dimension) throw new ArgumentException("Dimension mismatch");
            if (topK <= 0) return new List<SearchResult>();

            if (Metric == VectorMetric.Cosine)
            {
                float norm = VectorMath.ComputeNorm(query);
                if (norm > 1e-6f)
                {
                    for (int i = 0; i < query.Length; i++) query[i] /= norm;
                }
            }

            if (_entryPointId == -1) return new List<SearchResult>();

            _lock.EnterReadLock();
            try
            {
                int currObj = _entryPointId;
                float dist = Dist(query, GetVectorSpan(currObj));

                for (int l = _maxLayer; l > 0; l--)
                {
                    bool changed = true;
                    while (changed)
                    {
                        changed = false;
                        var neighbors = _nodes[currObj].GetNeighbors(l);
                        foreach (var neighborId in neighbors)
                        {
                            float d = Dist(query, GetVectorSpan(neighborId));
                            if (d < dist)
                            {
                                dist = d;
                                currObj = neighborId;
                                changed = true;
                            }
                        }
                    }
                }

                var candidates = SearchLayer(currObj, query, Math.Max(EfSearch, topK), 0);

                var results = new List<SearchResult>();
                foreach (var c in candidates)
                {
                    if (_nodes[c.Id].IsDeleted) continue;

                    float score = Metric switch
                    {
                        VectorMetric.L2 => -c.Distance,
                        VectorMetric.InnerProduct => -c.Distance,
                        VectorMetric.Cosine => 1.0f - c.Distance,
                        _ => c.Distance
                    };
                    results.Add(new SearchResult(_nodes[c.Id].ExternalId, score));
                    if (results.Count >= topK) break;
                }
                return results;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        private ReadOnlySpan<float> GetVectorSpan(int nodeId)
        {
            // Use CollectionsMarshal for unsafe Span access without copy
            var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_flatVectors);
            return span.Slice(nodeId * Dimension, Dimension);
        }

        private List<Candidate> SearchLayer(int entryPointId, ReadOnlySpan<float> query, int ef, int level)
        {
            var vVisited = new HashSet<int>();
            var C = new PriorityQueue<Candidate, float>();
            var W = new List<Candidate>();

            float dist = Dist(query, GetVectorSpan(entryPointId));
            var entryCand = new Candidate(entryPointId, dist);
            C.Enqueue(entryCand, dist);
            W.Add(entryCand);
            vVisited.Add(entryPointId);

            while (C.Count > 0)
            {
                var c = C.Dequeue();
                W.Sort((a, b) => a.Distance.CompareTo(b.Distance));
                var furthest = W[W.Count - 1];

                if (c.Distance > furthest.Distance) break;

                var neighbors = _nodes[c.Id].GetNeighbors(level);
                foreach (var neighborId in neighbors)
                {
                    if (!vVisited.Contains(neighborId))
                    {
                        vVisited.Add(neighborId);
                        float d = Dist(query, GetVectorSpan(neighborId));

                        if (d < furthest.Distance || W.Count < ef)
                        {
                            var newCand = new Candidate(neighborId, d);
                            C.Enqueue(newCand, d);
                            W.Add(newCand);
                            if (W.Count > ef)
                            {
                                W.Sort((a, b) => a.Distance.CompareTo(b.Distance));
                                W.RemoveAt(W.Count - 1);
                                furthest = W[W.Count - 1];
                            }
                        }
                    }
                }
            }
            return W;
        }

        private List<int> SelectNeighbors(List<Candidate> candidates, int m, int level)
        {
            candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            return candidates.Take(m).Select(c => c.Id).ToList();
        }

        private void PruneNeighbors(int nodeId, int level)
        {
            var node = _nodes[nodeId];
            var neighbors = node.GetNeighbors(level);
            var candidates = new List<Candidate>();

            var vOp = GetVectorSpan(nodeId);

            foreach (var nid in neighbors)
            {
                candidates.Add(new Candidate(nid, Dist(vOp, GetVectorSpan(nid))));
            }
            candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            var keep = candidates.Take(level == 0 ? M * 2 : M).Select(c => c.Id).ToList();
            node.SetNeighbors(level, keep);
        }

        private int GenerateRandomLevel()
        {
            double r = _rng.NextDouble();
            if (r < 1e-9) r = 1e-9;
            int level = (int)(-Math.Log(r) * _levelLambda);
            return level;
        }

        private float Dist(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
        {
            return Metric switch
            {
                VectorMetric.L2 => VectorMath.L2SquaredUnsafe(a, b),
                VectorMetric.Cosine => 1.0f - VectorMath.DotProductUnsafe(a, b), // Assuming normalized
                VectorMetric.InnerProduct => -VectorMath.DotProductUnsafe(a, b),
                _ => 0f
            };
        }

        public void Build() { _isBuilt = true; }
        public void Snapshot(string path) { }
        public void Load(string path) { }
        public IndexStats GetStats() { return new IndexStats(_nodes.Count, Dimension, Metric.ToString()); }

        private class Node
        {
            public string ExternalId { get; }
            // Vector removed
            public bool IsDeleted { get; set; }
            private readonly List<List<int>> _neighbors;

            public Node(string id, int maxLevel, int m)
            {
                ExternalId = id;
                _neighbors = new List<List<int>>(maxLevel + 1);
                for (int i = 0; i <= maxLevel; i++)
                {
                    _neighbors.Add(new List<int>(m));
                }
            }

            public List<int> GetNeighbors(int level)
            {
                if (level >= _neighbors.Count) return new List<int>();
                return _neighbors[level];
            }

            public void AddNeighbor(int level, int neighborId)
            {
                if (level < _neighbors.Count)
                {
                    var list = _neighbors[level];
                    if (!list.Contains(neighborId)) list.Add(neighborId);
                }
            }

            public void SetNeighbors(int level, List<int> neighbors)
            {
                if (level < _neighbors.Count) _neighbors[level] = neighbors;
            }
        }

        private struct Candidate
        {
            public int Id { get; }
            public float Distance { get; }
            public Candidate(int id, float distance) { Id = id; Distance = distance; }
        }
    }
}
