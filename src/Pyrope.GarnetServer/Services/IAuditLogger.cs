using System;
using System.Collections.Generic;
using Pyrope.GarnetServer.Model;

namespace Pyrope.GarnetServer.Services
{
    /// <summary>
    /// Interface for audit logging operations.
    /// </summary>
    public interface IAuditLogger
    {
        /// <summary>
        /// Logs an audit event.
        /// </summary>
        void Log(AuditEvent evt);

        /// <summary>
        /// Queries audit events with optional filters.
        /// </summary>
        /// <param name="tenantId">Filter by tenant ID (optional).</param>
        /// <param name="from">Start timestamp (optional).</param>
        /// <param name="to">End timestamp (optional).</param>
        /// <param name="action">Filter by action type (optional).</param>
        /// <param name="limit">Maximum number of results.</param>
        IEnumerable<AuditEvent> Query(
            string? tenantId = null,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            string? action = null,
            int limit = 100);

        /// <summary>
        /// Gets the total count of audit events.
        /// </summary>
        int Count { get; }
    }
}
