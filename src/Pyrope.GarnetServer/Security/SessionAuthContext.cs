using System.Threading;

namespace Pyrope.GarnetServer.Security
{
    /// <summary>
    /// Stores the authenticated tenant context for the current Garnet connection session.
    /// Uses AsyncLocal so the value flows through async continuations and task dispatches,
    /// which is necessary because Garnet may execute custom command callbacks on a different
    /// thread than the one that processed the AUTH command.
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
