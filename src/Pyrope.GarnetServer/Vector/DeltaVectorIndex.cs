using System;
using System.Collections.Generic;
using System.Linq;

namespace Pyrope.GarnetServer.Vector
{
    public class DeltaVectorIndex : IVectorIndex
    {
        private readonly IVectorIndex _head;
        private readonly IVectorIndex _tail;

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
            // Writes go to Head (Mutable)
            _head.Add(id, vector);
            
            // Note: If ID exists in Tail, we effectively shadow it because Search checks Head first/prioritizes Head.
        }

        public void Upsert(string id, float[] vector)
        {
            _head.Upsert(id, vector);
        }

        public bool Delete(string id)
        {
            // Propagate delete to both to ensure it's gone
            // In a real LSM, we might write a tombstone to Head.
            // But since our underlying indices support Delete, we attempt both.
            bool h = _head.Delete(id);
            bool t = _tail.Delete(id);
            return h || t;
        }

        public IReadOnlyList<SearchResult> Search(float[] query, int topK, SearchOptions? options = null)
        {
            // 1. Search Head
            var headResults = _head.Search(query, topK, options);
            
            // 2. Search Tail details
            // We might need to fetch more from tail if head has deletions (tombstones), 
            // but for now we fetch topK from each and merge.
            // Optimization: If Head has enough results with very high score, we might verify threshold.
            var tailResults = _tail.Search(query, topK, options);

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
            _head.Build();
            _tail.Build();
        }

        public void Snapshot(string path)
        {
            // Snapshot both components with suffixes
            _head.Snapshot(path + ".head");
            _tail.Snapshot(path + ".tail");

            // Write manifest to original path (satisfies "File.Exists(path)" and metadata)
            System.IO.File.WriteAllText(path, "{\"Type\": \"Delta\", \"Head\": \".head\", \"Tail\": \".tail\"}");
        }

        public void Load(string path)
        {
            // Load both components
            // Note: This assumes files exist.
            // If strictly new, might fail if files don't exist.
            // But Load usually assumes existing snapshot.
            if (System.IO.File.Exists(path + ".head"))
            {
                _head.Load(path + ".head");
            }
            if (System.IO.File.Exists(path + ".tail"))
            {
                _tail.Load(path + ".tail");
            }
        }

        public IndexStats GetStats()
        {
            var h = _head.GetStats();
            var t = _tail.GetStats();
            // Count is approx sum (minus duplicates). accurate count is expensive without keeping ID set.
            // Just sum for now.
            return new IndexStats(h.Count + t.Count, h.Dimension, h.Metric);
        }
    }
}
