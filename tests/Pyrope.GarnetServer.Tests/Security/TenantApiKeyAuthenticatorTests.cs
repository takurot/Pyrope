using Microsoft.Extensions.Options;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Security;
using Pyrope.GarnetServer.Services;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Security
{
    public class TenantApiKeyAuthenticatorTests
    {
        private readonly TenantRegistry _registry = new();

        public TenantApiKeyAuthenticatorTests()
        {
            _registry.TryCreate("tenant1", new TenantQuota(), out _, apiKey: "secret123");
            _registry.TryCreate("tenant_nokey", new TenantQuota(), out _, apiKey: null);
        }

        [Fact]
        public void TryAuthenticate_ValidKey_ReturnsTrue()
        {
            var auth = new TenantApiKeyAuthenticator(_registry, Options.Create(new ApiKeyAuthOptions { Enabled = true }));
            var success = auth.TryAuthenticate("tenant1", "secret123", out var err);

            Assert.True(success);
            Assert.Null(err);
        }

        [Fact]
        public void TryAuthenticate_WhenDisabled_DoesNotFailOpen()
        {
            // Even if Enabled is false, it must NOT fail-open for invalid/missing keys
            var auth = new TenantApiKeyAuthenticator(_registry, Options.Create(new ApiKeyAuthOptions { Enabled = false }));

            var successUnknown = auth.TryAuthenticate("unknown", "secret123", out var err1);
            Assert.False(successUnknown);
            Assert.Equal("Tenant not found.", err1);

            var successWrongKey = auth.TryAuthenticate("tenant1", "wrong", out var err2);
            Assert.False(successWrongKey);
            Assert.Equal("Invalid API key.", err2);

            var successEmptyKey = auth.TryAuthenticate("tenant1", "", out var err3);
            Assert.False(successEmptyKey);
            Assert.Equal("Missing API key.", err3);
        }

        [Fact]
        public void TryAuthenticate_MissingTenantOrKey_ReturnsFalse()
        {
            var auth = new TenantApiKeyAuthenticator(_registry, Options.Create(new ApiKeyAuthOptions { Enabled = true }));

            Assert.False(auth.TryAuthenticate("", "secret123", out var err1));
            Assert.Equal("Missing tenant ID.", err1);

            Assert.False(auth.TryAuthenticate("tenant1", "", out var err2));
            Assert.Equal("Missing API key.", err2);

            Assert.False(auth.TryAuthenticate("unknown", "secret123", out var err3));
            Assert.Equal("Tenant not found.", err3);

            Assert.False(auth.TryAuthenticate("tenant_nokey", "secret123", out var err4));
            Assert.Equal("Tenant API key not configured.", err4);

            Assert.False(auth.TryAuthenticate("tenant1", "wrongkey", out var err5));
            Assert.Equal("Invalid API key.", err5);
        }
    }
}
