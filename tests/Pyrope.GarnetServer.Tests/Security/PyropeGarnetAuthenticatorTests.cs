using System;
using Microsoft.Extensions.Options;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Security;
using Pyrope.GarnetServer.Services;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Security
{
    public class PyropeGarnetAuthenticatorTests : IDisposable
    {
        private readonly TenantRegistry _tenantRegistry = new();
        private readonly ITenantAuthenticator _tenantAuthenticator;

        public PyropeGarnetAuthenticatorTests()
        {
            SessionAuthContext.Reset();
            var opts = Options.Create(new ApiKeyAuthOptions { Enabled = true });
            _tenantAuthenticator = new TenantApiKeyAuthenticator(_tenantRegistry, opts);
            _tenantRegistry.TryCreate("tenant1", new TenantQuota(), out _, apiKey: "secret123");
        }

        public void Dispose() => SessionAuthContext.Reset();

        [Fact]
        public void Authenticate_AclFormat_ValidCredentials_ReturnsTrue()
        {
            // ACL-format: username=tenantId, password=apiKey (Redis 6 style)
            var authenticator = new PyropeGarnetAuthenticator(_tenantAuthenticator);
            bool result = authenticator.Authenticate(
                System.Text.Encoding.UTF8.GetBytes("tenant1"),
                System.Text.Encoding.UTF8.GetBytes("secret123"));

            Assert.True(result);
            Assert.True(authenticator.IsAuthenticated); // always true (Garnet-level open)
        }

        [Fact]
        public void Authenticate_AclFormat_ValidCredentials_SetsSessionContext()
        {
            // ACL-format: username=tenantId, password=apiKey (Redis 6 style)
            var authenticator = new PyropeGarnetAuthenticator(_tenantAuthenticator);
            authenticator.Authenticate(
                System.Text.Encoding.UTF8.GetBytes("tenant1"),
                System.Text.Encoding.UTF8.GetBytes("secret123"));

            Assert.True(SessionAuthContext.IsAuthenticated);
            Assert.Equal("tenant1", SessionAuthContext.AuthenticatedTenantId);
        }

        [Fact]
        public void Authenticate_WrongPassword_ReturnsTrue_ButDoesNotSetSession()
        {
            // Garnet layer stays open even for bad creds; VEC commands enforce auth.
            var authenticator = new PyropeGarnetAuthenticator(_tenantAuthenticator);
            bool result = authenticator.Authenticate(
                System.Text.Encoding.UTF8.GetBytes("tenant1"),
                System.Text.Encoding.UTF8.GetBytes("wrongkey"));

            Assert.True(result);  // always true at Garnet level
            Assert.False(SessionAuthContext.IsAuthenticated); // session NOT set on failure
        }

        [Fact]
        public void Authenticate_UnknownTenant_ReturnsTrue_ButDoesNotSetSession()
        {
            var authenticator = new PyropeGarnetAuthenticator(_tenantAuthenticator);
            bool result = authenticator.Authenticate(
                System.Text.Encoding.UTF8.GetBytes("unknowntenant"),
                System.Text.Encoding.UTF8.GetBytes("secret123"));

            Assert.True(result);  // always true at Garnet level
            Assert.False(SessionAuthContext.IsAuthenticated);
        }

        [Fact]
        public void Authenticate_PasswordOnly_TenantColonFormat_Works()
        {
            // Support "AUTH tenant1:secret123" (single-arg Redis AUTH, empty username)
            var authenticator = new PyropeGarnetAuthenticator(_tenantAuthenticator);
            bool result = authenticator.Authenticate(
                System.ReadOnlySpan<byte>.Empty,
                System.Text.Encoding.UTF8.GetBytes("tenant1:secret123"));

            Assert.True(result);
            Assert.Equal("tenant1", SessionAuthContext.AuthenticatedTenantId);
        }

        [Fact]
        public void Authenticate_DefaultUsername_TreatedAsSingleArg()
        {
            // Garnet sends "default" as username for single-arg AUTH when HasACLSupport=true
            var authenticator = new PyropeGarnetAuthenticator(_tenantAuthenticator);
            bool result = authenticator.Authenticate(
                System.Text.Encoding.UTF8.GetBytes("default"),
                System.Text.Encoding.UTF8.GetBytes("tenant1:secret123"));

            Assert.True(result);
            Assert.Equal("tenant1", SessionAuthContext.AuthenticatedTenantId);
        }

        [Fact]
        public void Authenticate_PasswordInUsername_StackExchangeRedisFormat_Works()
        {
            // StackExchange.Redis with HasACLSupport=false sends "tenantId:apiKey" in USERNAME field
            // and empty string in PASSWORD field.
            var authenticator = new PyropeGarnetAuthenticator(_tenantAuthenticator);
            bool result = authenticator.Authenticate(
                System.Text.Encoding.UTF8.GetBytes("tenant1:secret123"),
                System.ReadOnlySpan<byte>.Empty);

            Assert.True(result);
            Assert.Equal("tenant1", SessionAuthContext.AuthenticatedTenantId);
        }

        [Fact]
        public void Authenticate_PasswordInUsername_WrongKey_DoesNotSetSession()
        {
            var authenticator = new PyropeGarnetAuthenticator(_tenantAuthenticator);
            bool result = authenticator.Authenticate(
                System.Text.Encoding.UTF8.GetBytes("tenant1:wrongkey"),
                System.ReadOnlySpan<byte>.Empty);

            Assert.True(result); // always true at Garnet level
            Assert.False(SessionAuthContext.IsAuthenticated);
        }

        [Fact]
        public void CanAuthenticate_IsTrue()
        {
            var authenticator = new PyropeGarnetAuthenticator(_tenantAuthenticator);
            Assert.True(authenticator.CanAuthenticate);
        }

        [Fact]
        public void IsAuthenticated_AlwaysTrue_GarnetLevelOpen()
        {
            // IsAuthenticated is always true so Garnet doesn't block commands.
            // Actual tenant auth is enforced by VEC command handlers.
            var authenticator = new PyropeGarnetAuthenticator(_tenantAuthenticator);
            Assert.True(authenticator.IsAuthenticated);
        }
    }
}
