using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Pyrope.GarnetServer.Model;

namespace Pyrope.GarnetServer.Services
{
    /// <summary>
    /// In-memory audit logger with optional file persistence.
    /// </summary>
    public sealed class AuditLogger : IAuditLogger
    {
        private readonly ConcurrentQueue<AuditEvent> _events = new();
        private readonly object _fileLock = new();
        private readonly string? _logFilePath;
        private readonly int _maxInMemoryEvents;
        private int _eventCount = 0;
        private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB

        /// <summary>
        /// Creates an audit logger.
        /// </summary>
        /// <param name="logFilePath">Optional path for JSONL persistence.</param>
        /// <param name="maxInMemoryEvents">Maximum events to keep in memory.</param>
        public AuditLogger(string? logFilePath = null, int maxInMemoryEvents = 10000)
        {
            _logFilePath = logFilePath;
            _maxInMemoryEvents = maxInMemoryEvents;
        }

        /// <inheritdoc/>
        public int Count => _eventCount;

        /// <inheritdoc/>
        public void Log(AuditEvent evt)
        {
            if (evt == null) throw new ArgumentNullException(nameof(evt));

            // Add to in-memory queue
            _events.Enqueue(evt);
            Interlocked.Increment(ref _eventCount);

            // Trim if exceeds max
            while (_events.Count > _maxInMemoryEvents)
            {
                if (_events.TryDequeue(out _))
                {
                    Interlocked.Decrement(ref _eventCount);
                }
            }

            // Persist to file if configured
            if (!string.IsNullOrWhiteSpace(_logFilePath))
            {
                PersistToFile(evt);
            }
        }

        /// <inheritdoc/>
        public IEnumerable<AuditEvent> Query(
            string? tenantId = null,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            string? action = null,
            int limit = 100)
        {
            var query = _events.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                query = query.Where(e => string.Equals(e.TenantId, tenantId, StringComparison.Ordinal));
            }

            if (from.HasValue)
            {
                query = query.Where(e => e.Timestamp >= from.Value);
            }

            if (to.HasValue)
            {
                query = query.Where(e => e.Timestamp <= to.Value);
            }

            if (!string.IsNullOrWhiteSpace(action))
            {
                query = query.Where(e => string.Equals(e.Action, action, StringComparison.OrdinalIgnoreCase));
            }

            // Return in reverse chronological order
            return query.OrderByDescending(e => e.Timestamp).Take(limit);
        }

        private void PersistToFile(AuditEvent evt)
        {
            try
            {
                var json = JsonSerializer.Serialize(new
                {
                    evt.EventId,
                    Timestamp = evt.Timestamp.ToString("o"),
                    evt.TenantId,
                    evt.UserId,
                    evt.Action,
                    evt.ResourceType,
                    evt.ResourceId,
                    evt.Details,
                    evt.IpAddress,
                    evt.Success
                });

                lock (_fileLock)
                {
                    var dir = Path.GetDirectoryName(_logFilePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    FileInfo fi = new FileInfo(_logFilePath!);
                    if (fi.Exists && fi.Length > MaxFileSizeBytes)
                    {
                        // Simple rotation: rename current to .old and start fresh
                        var oldPath = _logFilePath + ".old";
                        if (File.Exists(oldPath)) File.Delete(oldPath);
                        File.Move(_logFilePath!, oldPath);
                    }

                    File.AppendAllText(_logFilePath!, json + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                // Log to console on failure (don't throw - audit logging shouldn't break operations)
                Console.Error.WriteLine($"[AuditLogger] Failed to persist event: {ex.Message}");
            }
        }
    }
}
