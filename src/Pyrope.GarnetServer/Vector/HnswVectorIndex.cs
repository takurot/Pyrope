using System;
using System.Collections.Generic;
using System.Threading;

namespace Pyrope.GarnetServer.Vector
{
    public class HnswVectorIndex : IVectorIndex
    {
        private readonly ReaderWriterLockSlim _lock = new();
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

        public void Add(string id, float[] vector)
        {
            if (vector == null) throw new ArgumentNullException(nameof(vector));
            if (vector.Length != Dimension) throw new ArgumentException("Dimension mismatch");

            // Normalize if Cosine to ensure correctness
            if (Metric == VectorMetric.Cosine)
            {
                // Note: Modifying input vector might surprise caller, but for now we clone if we need to normalize?
                // Or just normalize in place for efficiency as we own the data in the index.
                // Let's assume ownership for 'Add'.
                float norm = VectorMath.ComputeNorm(vector);
                if (norm > 1e-6f)
                {
                    for (int i = 0; i < vector.Length; i++) vector[i] /= norm;
                }
            }

            _lock.EnterWriteLock();
            try
            {
                if (_idMap.TryGetValue(id, out int existingId))
                {
                    // Update: Delete existing logic (Logical delete) then Add new
                    // Soft delete the existing node
                    _nodes[existingId].IsDeleted = true;
                    _idMap.Remove(id);
                }

                int newNodeId = _nodes.Count;
                int level = GenerateRandomLevel();
                var newNode = new Node(id, vector, level, M);

                _nodes.Add(newNode);
                _idMap[id] = newNodeId;

                int currObj = _entryPointId;

                // If entry point is deleted, we might need to find a new one, but for HNSW 
                // typically we just traverse. If valid entry point exists:
                if (currObj != -1)
                {
                    float dist = Dist(vector, _nodes[currObj].Vector);

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
                                if (_nodes[neighborId].IsDeleted) continue; // Skip deleted during traversal? 
                                // Actually, robust HNSW keeps deleted nodes in graph for connectivity until cleanup.
                                // But for greedy search we can still use them as bridges, but not as results.
                                // Standard HNSW: keep traversing deleted nodes.

                                float d = Dist(vector, _nodes[neighborId].Vector);
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
                    for (int l = Math.Min(level, _maxLayer); l >= 0; l--)
                    {
                        // Search for efConstruction neighbors
                        var candidates = SearchLayer(currObj, vector, EfConstruction, l);
                        var neighbors = SelectNeighbors(candidates, M, l); // Heuristic or simple

                        // Connect bidirectional
                        foreach (var neighborId in neighbors)
                        {
                            newNode.AddNeighbor(l, neighborId);
                            var neighbor = _nodes[neighborId];
                            neighbor.AddNeighbor(l, newNodeId);

                            // Prune connections of neighbor if too many
                            if (neighbor.GetNeighbors(l).Count > (l == 0 ? M * 2 : M))
                            {
                                PruneNeighbors(neighbor, l);
                            }
                        }

                        // Update currObj for next layer to the best candidate found
                        if (candidates.Count > 0)
                        {
                            currObj = candidates[0].Id;
                        }
                    }

                    // Update entry point if new node is higher
                    if (level > _maxLayer)
                    {
                        _maxLayer = level;
                        _entryPointId = newNodeId;
                    }
                }
                else
                {
                    // First node
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
            Add(id, vector); // Add handles update via logical delete
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

            // Normalize query if Cosine
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
                float dist = Dist(query, _nodes[currObj].Vector);

                // 1. Zoom in Logic (Top -> 0)
                for (int l = _maxLayer; l > 0; l--)
                {
                    bool changed = true;
                    while (changed)
                    {
                        changed = false;
                        var neighbors = _nodes[currObj].GetNeighbors(l);
                        foreach (var neighborId in neighbors)
                        {
                            // We traverse deleted nodes as bridges
                            float d = Dist(query, _nodes[neighborId].Vector);
                            if (d < dist)
                            {
                                dist = d;
                                currObj = neighborId;
                                changed = true;
                            }
                        }
                    }
                }

                // 2. Layer 0 Search with efSearch
                var candidates = SearchLayer(currObj, query, Math.Max(EfSearch, topK), 0);

                // Return Top K (filtering deleted)
                var results = new List<SearchResult>();
                foreach (var c in candidates)
                {
                    if (_nodes[c.Id].IsDeleted) continue; // Skip deleted docs in result

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

        // --- Helpers ---

        private List<Candidate> SearchLayer(int entryPointId, float[] query, int ef, int level)
        {
            var vVisited = new HashSet<int>();
            // Use PriorityQueue for Candidates (Min-Heap by distance)
            var C = new PriorityQueue<Candidate, float>();
            // Use PriorityQueue for Results W (Max-Heap by distance to find furthest)
            // But we also need to iterate/remove from W. Standard PQ doesn't support random remove/search efficiently.
            // Using a List + Sort or SortedSet is common. 
            // Let's use a List and specific insertion logic for 'ef' maintenance to be robust safe compared to SortedList.
            var W = new List<Candidate>();

            float dist = Dist(query, _nodes[entryPointId].Vector);
            var entryCand = new Candidate(entryPointId, dist);
            C.Enqueue(entryCand, dist);
            W.Add(entryCand);
            vVisited.Add(entryPointId);

            while (C.Count > 0)
            {
                var c = C.Dequeue();

                // Get furthest in W
                W.Sort((a, b) => a.Distance.CompareTo(b.Distance)); // Sort ASC
                var furthest = W[W.Count - 1];

                if (c.Distance > furthest.Distance) break;

                var neighbors = _nodes[c.Id].GetNeighbors(level);
                foreach (var neighborId in neighbors)
                {
                    if (!vVisited.Contains(neighborId))
                    {
                        vVisited.Add(neighborId);
                        float d = Dist(query, _nodes[neighborId].Vector);

                        if (d < furthest.Distance || W.Count < ef)
                        {
                            var newCand = new Candidate(neighborId, d);
                            C.Enqueue(newCand, d);

                            // Insert into W
                            W.Add(newCand);
                            // Keep W size <= ef
                            if (W.Count > ef)
                            {
                                // Remove furthest
                                W.Sort((a, b) => a.Distance.CompareTo(b.Distance));
                                W.RemoveAt(W.Count - 1);
                                furthest = W[W.Count - 1]; // Update furthest
                            }
                        }
                    }
                }
            }

            return W;
        }

        private List<int> SelectNeighbors(List<Candidate> candidates, int m, int level)
        {
            // Simple: return top M candidates
            // Candidates (W) are already roughly sorted or we sort them
            candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            return candidates.Take(m).Select(c => c.Id).ToList();
        }

        private void PruneNeighbors(Node node, int level)
        {
            var neighbors = node.GetNeighbors(level);
            // Re-calc distances to this node and keep nearest M
            var candidates = new List<Candidate>();
            foreach (var nid in neighbors)
            {
                candidates.Add(new Candidate(nid, Dist(node.Vector, _nodes[nid].Vector)));
            }
            candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            var keep = candidates.Take(level == 0 ? M * 2 : M).Select(c => c.Id).ToList();
            node.SetNeighbors(level, keep);
        }

        // Removed GetFurthest as we use List directly now

        private int GenerateRandomLevel()
        {
            double r = _rng.NextDouble();
            if (r < 1e-9) r = 1e-9; // Avoid log(0)
            int level = (int)(-Math.Log(r) * _levelLambda);
            return level;
        }

        private float Dist(float[] a, float[] b)
        {
            return Metric switch
            {
                VectorMetric.L2 => VectorMath.L2SquaredUnsafe(a, b),
                VectorMetric.Cosine => 1.0f - VectorMath.DotProductUnsafe(a, b), // Assuming normalized. Distance = 1 - CosSim
                VectorMetric.InnerProduct => -VectorMath.DotProductUnsafe(a, b), // Higher IP = Lower Dist
                _ => 0f
            };
        }

        public void Build()
        {
            // No-op (incremental)
            _isBuilt = true;
        }

        public void Snapshot(string path)
        {
            // TODO: Serialize _nodes and graph
        }

        public void Load(string path)
        {
            // TODO: Deserialize
        }

        public IndexStats GetStats()
        {
            return new IndexStats(_nodes.Count, Dimension, Metric.ToString());
        }

        private class Node
        {
            public string ExternalId { get; }
            public float[] Vector { get; }
            public bool IsDeleted { get; set; }
            private readonly List<List<int>> _neighbors;

            public Node(string id, float[] vector, int maxLevel, int m)
            {
                ExternalId = id;
                Vector = vector;
                _neighbors = new List<List<int>>(maxLevel + 1);
                for (int i = 0; i <= maxLevel; i++)
                {
                    _neighbors.Add(new List<int>(m)); // Capacity hint
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
