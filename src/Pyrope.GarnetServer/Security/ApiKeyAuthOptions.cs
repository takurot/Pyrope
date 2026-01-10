namespace Pyrope.GarnetServer.Security
{
    public sealed class ApiKeyAuthOptions
    {
        /// <summary>
        /// When true, API key authentication is enforced for /v1/* endpoints.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Admin API key used to authenticate HTTP management endpoints.
        /// </summary>
        public string AdminApiKey { get; set; } = "";
    }
}

