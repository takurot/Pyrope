using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Pyrope.GarnetServer.Model;

namespace Pyrope.GarnetServer.Services
{
    public sealed class BillingLogStore : IBillingLogStore
    {
        private const string GenesisHash = "GENESIS";
        private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB

        private readonly ConcurrentDictionary<string, TenantLogState> _states = new(StringComparer.Ordinal);
        private readonly object _fileLock = new();
        private readonly BillingOptions _options;

        public BillingLogStore(IOptions<BillingOptions> options)
        {
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        }

        public BillingLogEntry AppendSnapshot(TenantBillingUsage usage, DateTimeOffset timestamp)
        {
            if (usage == null) throw new ArgumentNullException(nameof(usage));

            var state = _states.GetOrAdd(usage.TenantId, _ => new TenantLogState());
            BillingLogEntry entry;

            lock (state.Sync)
            {
                var prevHash = state.LastHash ?? GenesisHash;
                var payload = BuildPayload(
                    timestamp,
                    usage.TenantId,
                    usage.RequestsTotal,
                    usage.CacheHits,
                    usage.CacheMisses,
                    usage.ComputeCostUnits,
                    usage.ComputeSeconds,
                    usage.VectorStorageBytes,
                    usage.SnapshotStorageBytes);

                var hash = ComputeHash(prevHash, payload);

                entry = new BillingLogEntry(
                    EntryId: Guid.NewGuid().ToString("N"),
                    Timestamp: timestamp,
                    TenantId: usage.TenantId,
                    RequestsTotal: usage.RequestsTotal,
                    CacheHits: usage.CacheHits,
                    CacheMisses: usage.CacheMisses,
                    ComputeCostUnits: usage.ComputeCostUnits,
                    ComputeSeconds: usage.ComputeSeconds,
                    VectorStorageBytes: usage.VectorStorageBytes,
                    SnapshotStorageBytes: usage.SnapshotStorageBytes,
                    PrevHash: prevHash,
                    Hash: hash);

                state.Entries.Enqueue(entry);
                state.LastHash = hash;

                while (state.Entries.Count > _options.MaxInMemoryEntries)
                {
                    state.Entries.TryDequeue(out _);
                }
            }

            if (!string.IsNullOrWhiteSpace(_options.LogPath))
            {
                PersistToFile(entry);
            }

            return entry;
        }

        public IReadOnlyList<BillingLogEntry> Query(string tenantId, int limit = 100)
        {
            if (string.IsNullOrWhiteSpace(tenantId)) return Array.Empty<BillingLogEntry>();
            if (limit < 1) limit = 1;
            if (limit > 1000) limit = 1000;

            if (!_states.TryGetValue(tenantId, out var state))
            {
                return Array.Empty<BillingLogEntry>();
            }

            BillingLogEntry[] entries;
            lock (state.Sync)
            {
                entries = state.Entries.ToArray();
            }
            return entries
                .OrderByDescending(e => e.Timestamp)
                .Take(limit)
                .ToList();
        }

        public bool VerifyChain(string tenantId)
        {
            if (string.IsNullOrWhiteSpace(tenantId)) return true;
            if (!_states.TryGetValue(tenantId, out var state))
            {
                return true;
            }

            BillingLogEntry[] entries;
            lock (state.Sync)
            {
                entries = state.Entries.ToArray();
            }
            var prevHash = GenesisHash;

            foreach (var entry in entries)
            {
                var payload = BuildPayload(
                    entry.Timestamp,
                    entry.TenantId,
                    entry.RequestsTotal,
                    entry.CacheHits,
                    entry.CacheMisses,
                    entry.ComputeCostUnits,
                    entry.ComputeSeconds,
                    entry.VectorStorageBytes,
                    entry.SnapshotStorageBytes);

                var expected = ComputeHash(prevHash, payload);
                if (!string.Equals(expected, entry.Hash, StringComparison.Ordinal))
                {
                    return false;
                }

                prevHash = entry.Hash;
            }

            return true;
        }

        private static string BuildPayload(
            DateTimeOffset timestamp,
            string tenantId,
            long requestsTotal,
            long cacheHits,
            long cacheMisses,
            double computeCostUnits,
            double computeSeconds,
            long vectorStorageBytes,
            long snapshotStorageBytes)
        {
            return string.Join("|", new[]
            {
                timestamp.ToString("o"),
                tenantId,
                requestsTotal.ToString(CultureInfo.InvariantCulture),
                cacheHits.ToString(CultureInfo.InvariantCulture),
                cacheMisses.ToString(CultureInfo.InvariantCulture),
                computeCostUnits.ToString("G17", CultureInfo.InvariantCulture),
                computeSeconds.ToString("G17", CultureInfo.InvariantCulture),
                vectorStorageBytes.ToString(CultureInfo.InvariantCulture),
                snapshotStorageBytes.ToString(CultureInfo.InvariantCulture)
            });
        }

        private static string ComputeHash(string prevHash, string payload)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(prevHash + "|" + payload);
            var hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash);
        }

        private void PersistToFile(BillingLogEntry entry)
        {
            try
            {
                var json = JsonSerializer.Serialize(new
                {
                    entry.EntryId,
                    Timestamp = entry.Timestamp.ToString("o"),
                    entry.TenantId,
                    entry.RequestsTotal,
                    entry.CacheHits,
                    entry.CacheMisses,
                    entry.ComputeCostUnits,
                    entry.ComputeSeconds,
                    entry.VectorStorageBytes,
                    entry.SnapshotStorageBytes,
                    entry.PrevHash,
                    entry.Hash
                });

                lock (_fileLock)
                {
                    var dir = Path.GetDirectoryName(_options.LogPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    FileInfo fi = new FileInfo(_options.LogPath!);
                    if (fi.Exists && fi.Length > MaxFileSizeBytes)
                    {
                        var oldPath = _options.LogPath + ".old";
                        if (File.Exists(oldPath)) File.Delete(oldPath);
                        File.Move(_options.LogPath!, oldPath);
                    }

                    File.AppendAllText(_options.LogPath!, json + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[BillingLogStore] Failed to persist entry: {ex.Message}");
            }
        }

        private sealed class TenantLogState
        {
            public ConcurrentQueue<BillingLogEntry> Entries { get; } = new();
            public string? LastHash { get; set; }
            public object Sync { get; } = new();
        }
    }
}
