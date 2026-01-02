using System;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Services;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Services
{
    public class TenantQuotaEnforcerTests
    {
        [Fact]
        public void TryBeginRequest_EnforcesQpsLimit()
        {
            var registry = new TenantRegistry();
            registry.TryCreate("tenant1", new TenantQuota { MaxQps = 2 }, out _);
            var timeProvider = new TestTimeProvider(1000);
            var enforcer = new TenantQuotaEnforcer(registry, timeProvider);

            Assert.True(enforcer.TryBeginRequest("tenant1", out var lease1, out _, out _));
            Assert.True(enforcer.TryBeginRequest("tenant1", out var lease2, out _, out _));

            Assert.False(enforcer.TryBeginRequest("tenant1", out var lease3, out var errorCode, out var errorMessage));
            Assert.Null(lease3);
            Assert.Equal("VEC_ERR_QUOTA", errorCode);
            Assert.Contains("QPS", errorMessage, StringComparison.OrdinalIgnoreCase);

            lease1?.Dispose();
            lease2?.Dispose();

            timeProvider.AdvanceSeconds(1);
            Assert.True(enforcer.TryBeginRequest("tenant1", out var lease4, out _, out _));
            lease4?.Dispose();
        }

        [Fact]
        public void TryBeginRequest_EnforcesConcurrencyLimit()
        {
            var registry = new TenantRegistry();
            registry.TryCreate("tenant1", new TenantQuota { MaxConcurrentRequests = 1 }, out _);
            var timeProvider = new TestTimeProvider(1000);
            var enforcer = new TenantQuotaEnforcer(registry, timeProvider);

            Assert.True(enforcer.TryBeginRequest("tenant1", out var lease1, out _, out _));
            Assert.False(enforcer.TryBeginRequest("tenant1", out var lease2, out var errorCode, out var errorMessage));
            Assert.Null(lease2);
            Assert.Equal("VEC_ERR_BUSY", errorCode);
            Assert.Contains("concurrency", errorMessage, StringComparison.OrdinalIgnoreCase);

            lease1?.Dispose();

            Assert.True(enforcer.TryBeginRequest("tenant1", out var lease3, out _, out _));
            lease3?.Dispose();
        }

        private sealed class TestTimeProvider : ITimeProvider
        {
            private long _seconds;

            public TestTimeProvider(long seconds)
            {
                _seconds = seconds;
            }

            public long GetUnixTimeSeconds()
            {
                return _seconds;
            }

            public void AdvanceSeconds(long seconds)
            {
                _seconds += seconds;
            }
        }
    }
}
