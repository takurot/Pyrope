using System.Threading;

namespace Pyrope.GarnetServer.Security
{
    /// <summary>
    /// Stores the authenticated tenant context for the current Garnet connection session.
    /// Uses AsyncLocal so the value is scoped to the current execution context and flows
    /// through async continuations. Garnet processes commands for a given connection on a
    /// dedicated session thread, so AUTH and subsequent VEC commands run in the same execution
    /// context — the AsyncLocal value set by AUTH is visible to VEC command handlers.
    /// Call Reset() in tests to ensure isolation between test cases.
    /// </summary>
    public static class SessionAuthContext
    {
        private static readonly AsyncLocal<string?> _authenticatedTenantId = new AsyncLocal<string?>();

        /// <summary>Whether the current execution context has an authenticated session.</summary>
        public static bool IsAuthenticated => _authenticatedTenantId.Value != null;

        /// <summary>The tenantId authenticated in this execution context, or null if not authenticated.</summary>
        public static string? AuthenticatedTenantId => _authenticatedTenantId.Value;

        /// <summary>Set the authenticated tenant for this execution context.</summary>
        public static void Set(string tenantId) => _authenticatedTenantId.Value = tenantId;

        /// <summary>Clear the session (unauthenticate). Call this in test teardown.</summary>
        public static void Reset() => _authenticatedTenantId.Value = null;
    }
}
