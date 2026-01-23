using System;
using System.Collections.Generic;
using System.Linq;

namespace Pyrope.GarnetServer.Vector
{
    public class DeltaVectorIndex : IVectorIndex
    {
        private readonly IVectorIndex _head;
        private readonly IVectorIndex _tail;
        private readonly System.Threading.ReaderWriterLockSlim _lock = new();

        public int Dimension => _head.Dimension;
        public VectorMetric Metric => _head.Metric;

        // For now, we expose Head for management, or we might need a composite strategy later.
        // But for Delta Indexing, usually Head is the active one.
        public DeltaVectorIndex(IVectorIndex head, IVectorIndex tail)
        {
            if (head.Dimension != tail.Dimension)
                throw new ArgumentException("Head and Tail dimensions must match");
            if (head.Metric != tail.Metric)
                throw new ArgumentException("Head and Tail metrics must match");

            _head = head;
            _tail = tail;
        }

        public void Add(string id, float[] vector)
        {
            _lock.EnterWriteLock();
            try
            {
                // Writes go to Head (Mutable)
                _head.Add(id, vector);

                // Note: If ID exists in Tail, we effectively shadow it because Search checks Head first/prioritizes Head.
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public void Upsert(string id, float[] vector)
        {
            _lock.EnterWriteLock();
            try
            {
                _head.Upsert(id, vector);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public bool Delete(string id)
        {
            _lock.EnterWriteLock();
            try
            {
                // Propagate delete to both to ensure it's gone
                // In a real LSM, we might write a tombstone to Head.
                // But since our underlying indices support Delete, we attempt both.
                bool h = _head.Delete(id);
                bool t = _tail.Delete(id);
                return h || t;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public IReadOnlyList<SearchResult> Search(float[] query, int topK, SearchOptions? options = null)
        {
            IReadOnlyList<SearchResult> headResults;
            IReadOnlyList<SearchResult> tailResults;

            _lock.EnterReadLock();
            try
            {
                // 1. Search Head
                headResults = _head.Search(query, topK, options);

                // 2. Search Tail details
                tailResults = _tail.Search(query, topK, options);
            }
            finally
            {
                _lock.ExitReadLock();
            }

            // 3. Merge & Deduplicate
            // Head wins on ID collision.
            var mergedMap = new Dictionary<string, SearchResult>();

            // Add Tail first
            foreach (var r in tailResults)
            {
                mergedMap[r.Id] = r;
            }

            // Add/Overwrite with Head
            foreach (var r in headResults)
            {
                mergedMap[r.Id] = r;
            }

            // 4. Sort and TopK
            var sorted = mergedMap.Values.ToList();

            // Sort based on Metric
            // BruteForceVectorIndex returns score where higher is better for ALL metrics.
            // L2: returns negative distance (e.g. -distance^2) so closer = higher (less negative).
            // IP/Cosine: higher is better.
            // Therefore, always sort Descending.
            sorted.Sort((a, b) => b.Score.CompareTo(a.Score));

            return sorted.Take(topK).ToList();
        }

        public void Build()
        {
            _lock.EnterWriteLock();
            try
            {
                // Compact: Move items from Head -> Tail
                // Compact: Move items from Head -> Tail
                IEnumerable<KeyValuePair<string, float[]>> items = null;

                if (_head is BruteForceVectorIndex bfHead)
                {
                    items = bfHead.Scan();
                }
                else if (_head is HnswVectorIndex hnswHead)
                {
                    items = hnswHead.Scan();
                }

                if (items != null)
                {
                    foreach (var kvp in items)
                    {
                        _tail.Add(kvp.Key, kvp.Value);
                        _head.Delete(kvp.Key);
                    }
                }

                _head.Build();
                _tail.Build();
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public void Snapshot(string path)
        {
            _lock.EnterReadLock();
            try
            {
                // 1. Performance snapshot to temporary paths
                string headPath = path + ".head";
                string tailPath = path + ".tail";
                string headTmp = headPath + ".tmp";
                string tailTmp = tailPath + ".tmp";

                // Snapshot both components with temporary suffixes
                _head.Snapshot(headTmp);
                _tail.Snapshot(tailTmp);

                // Atomic move
                if (System.IO.File.Exists(headPath)) System.IO.File.Delete(headPath);
                if (System.IO.File.Exists(tailPath)) System.IO.File.Delete(tailPath);
                System.IO.File.Move(headTmp, headPath);
                System.IO.File.Move(tailTmp, tailPath);

                // Write manifest to original path
                string manifestTmp = path + ".tmp";
                System.IO.File.WriteAllText(manifestTmp, "{\"Type\": \"Delta\", \"Head\": \".head\", \"Tail\": \".tail\"}");
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                System.IO.File.Move(manifestTmp, path);
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public void Load(string path)
        {
            _lock.EnterWriteLock();
            try
            {
                // Load both components
                if (System.IO.File.Exists(path + ".head"))
                {
                    _head.Load(path + ".head");
                }
                if (System.IO.File.Exists(path + ".tail"))
                {
                    _tail.Load(path + ".tail");
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
                var h = _head.GetStats();
                var t = _tail.GetStats();
                // Count is approx sum (minus duplicates). accurate count is expensive without keeping ID set.
                // Just sum for now.
                return new IndexStats(h.Count + t.Count, h.Dimension, h.Metric);
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }
}
