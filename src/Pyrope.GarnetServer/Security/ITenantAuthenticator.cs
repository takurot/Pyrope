namespace Pyrope.GarnetServer.Security
{
    public interface ITenantAuthenticator
    {
        bool TryAuthenticate(string tenantId, string? apiKey, out string? errorMessage);
    }
}

