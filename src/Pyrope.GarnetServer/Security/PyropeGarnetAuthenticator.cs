using System;
using System.Text;
using Garnet.server.Auth;

namespace Pyrope.GarnetServer.Security
{
    /// <summary>
    /// Garnet-level authenticator that maps standard Redis AUTH to Pyrope tenant authentication.
    /// Supports two formats:
    ///   - AUTH &lt;tenantId&gt; &lt;apiKey&gt;  (Redis 6 ACL format: username = tenantId)
    ///   - AUTH &lt;tenantId:apiKey&gt;     (single-argument: password = "tenantId:apiKey")
    ///
    /// On success, sets <see cref="SessionAuthContext"/> so subsequent VEC.* commands
    /// in the same execution context can skip the per-command API_KEY argument.
    /// </summary>
    public sealed class PyropeGarnetAuthenticator : IGarnetAuthenticator
    {
        private readonly ITenantAuthenticator _tenantAuthenticator;

        public PyropeGarnetAuthenticator(ITenantAuthenticator tenantAuthenticator)
        {
            _tenantAuthenticator = tenantAuthenticator ?? throw new ArgumentNullException(nameof(tenantAuthenticator));
        }

        /// <summary>
        /// Always true: Garnet-level access is open; tenant auth is enforced by VEC commands
        /// via per-command API_KEY or session context set by AUTH.
        /// </summary>
        public bool IsAuthenticated => true;
        public bool CanAuthenticate => true;

        /// <summary>
        /// False: uses Garnet's non-ACL AUTH path. Supports single-arg AUTH "tenantId:apiKey".
        /// ACL-format AUTH (separate username + password) is not supported in this release.
        /// </summary>
        public bool HasACLSupport => false;

        /// <summary>
        /// Processes the Redis AUTH command. Always returns true (Garnet-level access stays open).
        /// If credentials are valid, sets <see cref="SessionAuthContext"/> for the current thread,
        /// enabling VEC commands to skip per-command API_KEY for the session duration.
        /// Invalid credentials are NOT rejected at the Garnet level; the VEC command layer enforces auth.
        ///
        /// Supported AUTH formats:
        ///   AUTH tenant1:secret   → password-only, colon-separated tenantId:apiKey
        ///   AUTH tenant1 secret   → username=tenantId, password=apiKey (Redis 6 ACL format)
        ///
        /// Note: StackExchange.Redis with HasACLSupport=false sends the password string in the
        /// USERNAME field and empty string in PASSWORD. We detect this by checking if the username
        /// contains a colon (indicating tenantId:apiKey format).
        /// </summary>
        public bool Authenticate(ReadOnlySpan<byte> username, ReadOnlySpan<byte> password)
        {
            var usernameStr = Encoding.UTF8.GetString(username);
            var passwordStr = Encoding.UTF8.GetString(password);

            bool isDefaultOrEmpty = username.IsEmpty ||
                                    string.Equals(usernameStr, "default", StringComparison.OrdinalIgnoreCase);

            if (!isDefaultOrEmpty)
            {
                // Check if username contains colon: StackExchange.Redis sends "tenantId:apiKey"
                // in the USERNAME field when HasACLSupport=false.
                var sep = usernameStr.IndexOf(':');
                if (sep > 0)
                {
                    var tenantId = usernameStr[..sep];
                    var apiKey = usernameStr[(sep + 1)..];
                    if (_tenantAuthenticator.TryAuthenticate(tenantId, apiKey, out _))
                        SessionAuthContext.Set(tenantId);
                }
                else
                {
                    // ACL-format AUTH: username = tenantId, password = apiKey
                    if (_tenantAuthenticator.TryAuthenticate(usernameStr, passwordStr, out _))
                        SessionAuthContext.Set(usernameStr);
                }
            }
            else
            {
                // Empty username or "default": password = "tenantId:apiKey"
                var sep = passwordStr.IndexOf(':');
                if (sep > 0)
                {
                    var tenantId = passwordStr[..sep];
                    var apiKey = passwordStr[(sep + 1)..];
                    if (_tenantAuthenticator.TryAuthenticate(tenantId, apiKey, out _))
                        SessionAuthContext.Set(tenantId);
                }
            }

            // Always return true: Garnet connection stays open regardless of credential validity.
            // VEC commands enforce the actual tenant auth.
            return true;
        }
    }
}
