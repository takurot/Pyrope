namespace Pyrope.GarnetServer.Security
{
    /// <summary>
    /// Service for checking user permissions.
    /// </summary>
    public interface IAuthorizationService
    {
        /// <summary>
        /// Checks if an API key has a specific permission for a tenant.
        /// </summary>
        bool HasPermission(string tenantId, string apiKey, Permission permission);

        /// <summary>
        /// Gets the role associated with an API key for a tenant.
        /// Returns null if the API key is not found.
        /// </summary>
        Role? GetRole(string tenantId, string apiKey);

        /// <summary>
        /// Gets the user ID associated with an API key for a tenant.
        /// Returns null if the API key is not found.
        /// </summary>
        string? GetUserId(string tenantId, string apiKey);
    }
}
