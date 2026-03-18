using System;
using Garnet.server;
using Garnet.server.Auth;
using Garnet.server.Auth.Settings;

namespace Pyrope.GarnetServer.Security
{
    /// <summary>
    /// Garnet <see cref="IAuthenticationSettings"/> that creates a
    /// <see cref="PyropeGarnetAuthenticator"/> per session, enabling standard
    /// Redis AUTH commands to authenticate Pyrope tenants.
    /// </summary>
    public sealed class PyropeAuthenticationSettings : IAuthenticationSettings
    {
        private readonly ITenantAuthenticator _tenantAuthenticator;

        public PyropeAuthenticationSettings(ITenantAuthenticator tenantAuthenticator)
        {
            _tenantAuthenticator = tenantAuthenticator ?? throw new ArgumentNullException(nameof(tenantAuthenticator));
        }

        public IGarnetAuthenticator CreateAuthenticator(StoreWrapper storeWrapper)
            => new PyropeGarnetAuthenticator(_tenantAuthenticator);

        public void Dispose() { }
    }
}
