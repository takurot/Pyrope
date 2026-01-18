using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Extensions.Options;
using Pyrope.GarnetServer.Model;

namespace Pyrope.GarnetServer.Services
{
    public sealed class BillingMeter : IBillingMeter
    {
        private readonly ConcurrentDictionary<string, TenantUsageState> _usage = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, long> _snapshotSizes = new(StringComparer.Ordinal);
        private readonly BillingOptions _options;
        private readonly ITimeProvider _timeProvider;
        private readonly IBillingLogStore? _logStore;

        public BillingMeter(IOptions<BillingOptions> options, ITimeProvider timeProvider, IBillingLogStore? logStore = null)
        {
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            _logStore = logStore;
        }

        public BillingMeter(BillingOptions options, ITimeProvider? timeProvider = null, IBillingLogStore? logStore = null)
            : this(Options.Create(options ?? throw new ArgumentNullException(nameof(options))), timeProvider ?? new SystemTimeProvider(), logStore)
        {
        }

        public void RecordRequest(string tenantId, bool cacheHit)
        {
            if (string.IsNullOrWhiteSpace(tenantId)) return;
            var state = _usage.GetOrAdd(tenantId, _ => new TenantUsageState());
            var now = GetNow();

            lock (state.Sync)
            {
                state.RequestsTotal++;
                if (cacheHit) state.CacheHits++;
                else state.CacheMisses++;
                state.UpdatedAt = now;
            }

            MaybeLog(tenantId, state, now);
        }

        public void RecordCompute(string tenantId, double costUnits)
        {
            if (string.IsNullOrWhiteSpace(tenantId)) return;
            if (costUnits <= 0) return;

            var state = _usage.GetOrAdd(tenantId, _ => new TenantUsageState());
            var now = GetNow();
            var computeSeconds = costUnits * _options.CostUnitSeconds;

            lock (state.Sync)
            {
                state.ComputeCostUnits += costUnits;
                state.ComputeSeconds += computeSeconds;
                state.UpdatedAt = now;
            }

            MaybeLog(tenantId, state, now);
        }

        public void RecordVectorBytes(string tenantId, long deltaBytes)
        {
            if (string.IsNullOrWhiteSpace(tenantId)) return;
            if (deltaBytes == 0) return;

            var state = _usage.GetOrAdd(tenantId, _ => new TenantUsageState());
            var now = GetNow();

            lock (state.Sync)
            {
                state.VectorStorageBytes = Math.Max(0, state.VectorStorageBytes + deltaBytes);
                state.UpdatedAt = now;
            }

            MaybeLog(tenantId, state, now);
        }

        public void RecordSnapshot(string tenantId, string indexName, string snapshotPath)
        {
            if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(indexName)) return;
            if (string.IsNullOrWhiteSpace(snapshotPath)) return;

            var sizeBytes = GetSnapshotSize(snapshotPath);
            var key = $"{tenantId}:{indexName}";
            long delta = 0;

            _snapshotSizes.AddOrUpdate(
                key,
                _ =>
                {
                    delta = sizeBytes;
                    return sizeBytes;
                },
                (_, existing) =>
                {
                    delta = sizeBytes - existing;
                    return sizeBytes;
                });

            if (delta != 0)
            {
                var state = _usage.GetOrAdd(tenantId, _ => new TenantUsageState());
                var now = GetNow();
                lock (state.Sync)
                {
                    state.SnapshotStorageBytes = Math.Max(0, state.SnapshotStorageBytes + delta);
                    state.UpdatedAt = now;
                }
                MaybeLog(tenantId, state, now);
            }
        }

        public bool TryGetUsage(string tenantId, out TenantBillingUsage usage)
        {
            usage = null!;
            if (string.IsNullOrWhiteSpace(tenantId)) return false;
            if (!_usage.TryGetValue(tenantId, out var state))
            {
                return false;
            }

            lock (state.Sync)
            {
                usage = new TenantBillingUsage(
                    tenantId,
                    state.RequestsTotal,
                    state.CacheHits,
                    state.CacheMisses,
                    state.ComputeCostUnits,
                    state.ComputeSeconds,
                    state.VectorStorageBytes,
                    state.SnapshotStorageBytes,
                    state.UpdatedAt);
                return true;
            }
        }

        public IReadOnlyCollection<TenantBillingUsage> GetAllUsage()
        {
            var list = new List<TenantBillingUsage>();
            foreach (var pair in _usage)
            {
                var tenantId = pair.Key;
                var state = pair.Value;
                lock (state.Sync)
                {
                    list.Add(new TenantBillingUsage(
                        tenantId,
                        state.RequestsTotal,
                        state.CacheHits,
                        state.CacheMisses,
                        state.ComputeCostUnits,
                        state.ComputeSeconds,
                        state.VectorStorageBytes,
                        state.SnapshotStorageBytes,
                        state.UpdatedAt));
                }
            }

            return list;
        }

        public static long EstimateVectorRecordBytes(VectorRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (record.Deleted) return 0;

            long size = (long)record.Vector.Length * sizeof(float);

            if (!string.IsNullOrWhiteSpace(record.MetaJson))
            {
                size += Encoding.UTF8.GetByteCount(record.MetaJson);
            }

            foreach (var tag in record.Tags)
            {
                if (!string.IsNullOrEmpty(tag))
                {
                    size += Encoding.UTF8.GetByteCount(tag);
                }
            }

            foreach (var kvp in record.NumericFields)
            {
                size += Encoding.UTF8.GetByteCount(kvp.Key ?? string.Empty);
                size += sizeof(double);
            }

            return size;
        }

        private static long GetSnapshotSize(string path)
        {
            long total = 0;
            total += GetFileSize(path);
            total += GetFileSize(path + ".head");
            total += GetFileSize(path + ".tail");
            return total;
        }

        private static long GetFileSize(string path)
        {
            try
            {
                if (!File.Exists(path)) return 0;
                var info = new FileInfo(path);
                return info.Length;
            }
            catch
            {
                return 0;
            }
        }

        private DateTimeOffset GetNow()
        {
            var seconds = _timeProvider.GetUnixTimeSeconds();
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }

        private void MaybeLog(string tenantId, TenantUsageState state, DateTimeOffset now)
        {
            if (_logStore == null) return;
            if (_options.LogIntervalSeconds <= 0)
            {
                _logStore.AppendSnapshot(ToUsage(tenantId, state, now), now);
                return;
            }

            bool shouldLog;
            lock (state.Sync)
            {
                shouldLog = now - state.LastLoggedAt >= TimeSpan.FromSeconds(_options.LogIntervalSeconds);
                if (shouldLog)
                {
                    state.LastLoggedAt = now;
                }
            }

            if (!shouldLog) return;

            var usage = ToUsage(tenantId, state, now);
            _logStore.AppendSnapshot(usage, now);
        }

        private static TenantBillingUsage ToUsage(string tenantId, TenantUsageState state, DateTimeOffset now)
        {
            lock (state.Sync)
            {
                return new TenantBillingUsage(
                    tenantId,
                    state.RequestsTotal,
                    state.CacheHits,
                    state.CacheMisses,
                    state.ComputeCostUnits,
                    state.ComputeSeconds,
                    state.VectorStorageBytes,
                    state.SnapshotStorageBytes,
                    state.UpdatedAt == default ? now : state.UpdatedAt);
            }
        }

        private sealed class TenantUsageState
        {
            public long RequestsTotal { get; set; }
            public long CacheHits { get; set; }
            public long CacheMisses { get; set; }
            public double ComputeCostUnits { get; set; }
            public double ComputeSeconds { get; set; }
            public long VectorStorageBytes { get; set; }
            public long SnapshotStorageBytes { get; set; }
            public DateTimeOffset UpdatedAt { get; set; }
            public DateTimeOffset LastLoggedAt { get; set; }
            public object Sync { get; } = new();
        }
    }
}
