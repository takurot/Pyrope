using Pyrope.GarnetServer.Security;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Security
{
    public class SessionAuthContextTests
    {
        [Fact]
        public void NoSession_IsAuthenticatedFalse()
        {
            SessionAuthContext.Reset();
            Assert.False(SessionAuthContext.IsAuthenticated);
            Assert.Null(SessionAuthContext.AuthenticatedTenantId);
        }

        [Fact]
        public void SetSession_IsAuthenticatedTrue()
        {
            SessionAuthContext.Reset();
            SessionAuthContext.Set("tenant1");

            Assert.True(SessionAuthContext.IsAuthenticated);
            Assert.Equal("tenant1", SessionAuthContext.AuthenticatedTenantId);

            SessionAuthContext.Reset();
        }

        [Fact]
        public void Reset_ClearsSession()
        {
            SessionAuthContext.Set("tenant1");
            SessionAuthContext.Reset();

            Assert.False(SessionAuthContext.IsAuthenticated);
            Assert.Null(SessionAuthContext.AuthenticatedTenantId);
        }

        [Fact]
        public void Set_OverwritesPreviousSession()
        {
            SessionAuthContext.Set("tenant1");
            SessionAuthContext.Set("tenant2");

            Assert.Equal("tenant2", SessionAuthContext.AuthenticatedTenantId);

            SessionAuthContext.Reset();
        }
    }
}
