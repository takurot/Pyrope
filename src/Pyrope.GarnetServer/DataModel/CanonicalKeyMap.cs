using System;
using System.Collections.Concurrent;

namespace Pyrope.GarnetServer.DataModel
{
    /// <summary>
    /// P6-9: CanonicalKey Alias Map
    /// Maps source query hashes to canonical query hashes.
    /// Used for semantic aliasing (e.g., "cheap shoes" -> "affordable footwear").
    /// </summary>
    public sealed class CanonicalKeyMap
    {
        private readonly ConcurrentDictionary<ulong, AliasEntry> _aliases = new();

        /// <summary>
        /// Try to get the canonical hash for a given source hash.
        /// </summary>
        public bool TryGetCanonical(ulong sourceHash, out ulong canonicalHash, out float confidence)
        {
            if (_aliases.TryGetValue(sourceHash, out var entry))
            {
                canonicalHash = entry.CanonicalHash;
                confidence = entry.Confidence;
                return true;
            }

            canonicalHash = 0;
            confidence = 0;
            return false;
        }

        /// <summary>
        /// Set an alias mapping from source to canonical hash.
        /// </summary>
        public void SetAlias(ulong sourceHash, ulong canonicalHash, float confidence = 1.0f, TimeSpan? ttl = null)
        {
            var entry = new AliasEntry
            {
                CanonicalHash = canonicalHash,
                Confidence = confidence,
                ExpiresAt = ttl.HasValue ? DateTimeOffset.UtcNow.Add(ttl.Value) : DateTimeOffset.MaxValue
            };
            _aliases[sourceHash] = entry;
        }

        /// <summary>
        /// Remove an alias mapping.
        /// </summary>
        public bool RemoveAlias(ulong sourceHash)
        {
            return _aliases.TryRemove(sourceHash, out _);
        }

        /// <summary>
        /// Get the number of aliases stored.
        /// </summary>
        public int Count => _aliases.Count;

        /// <summary>
        /// Clear all aliases.
        /// </summary>
        public void Clear()
        {
            _aliases.Clear();
        }

        /// <summary>
        /// Clean up expired aliases.
        /// </summary>
        public int CleanupExpired()
        {
            var now = DateTimeOffset.UtcNow;
            var removed = 0;
            foreach (var kvp in _aliases)
            {
                if (kvp.Value.ExpiresAt < now)
                {
                    if (_aliases.TryRemove(kvp.Key, out _))
                    {
                        removed++;
                    }
                }
            }
            return removed;
        }

        private sealed class AliasEntry
        {
            public ulong CanonicalHash { get; set; }
            public float Confidence { get; set; }
            public DateTimeOffset ExpiresAt { get; set; }
        }
    }
}
