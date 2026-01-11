using Xunit;
using Pyrope.GarnetServer.Security;
using Pyrope.GarnetServer.Services;
using Pyrope.GarnetServer.Model;

namespace Pyrope.GarnetServer.Tests.Security
{
    public class RbacAuthorizationServiceTests
    {
        private readonly TenantUserRegistry _userRegistry;
        private readonly TenantRegistry _tenantRegistry;
        private readonly RbacAuthorizationService _authService;

        public RbacAuthorizationServiceTests()
        {
            _userRegistry = new TenantUserRegistry();
            _tenantRegistry = new TenantRegistry();
            _authService = new RbacAuthorizationService(_userRegistry, _tenantRegistry);
        }

        [Fact]
        public void GetRole_ReturnsNull_WhenApiKeyNotFound()
        {
            var role = _authService.GetRole("tenant1", "unknown-key");
            Assert.Null(role);
        }

        [Fact]
        public void GetRole_ReturnsUserRole_WhenApiKeyMatches()
        {
            // Arrange
            _tenantRegistry.TryCreate("tenant1", new TenantQuota(), out _, "tenant-api-key");
            _userRegistry.TryCreate("tenant1", "user1", Role.Operator, "user-api-key", out _);

            // Act
            var role = _authService.GetRole("tenant1", "user-api-key");

            // Assert
            Assert.Equal(Role.Operator, role);
        }

        [Fact]
        public void GetRole_ReturnsTenantAdmin_ForLegacyTenantApiKey()
        {
            // Arrange
            _tenantRegistry.TryCreate("tenant1", new TenantQuota(), out _, "legacy-tenant-key");

            // Act
            var role = _authService.GetRole("tenant1", "legacy-tenant-key");

            // Assert
            Assert.Equal(Role.TenantAdmin, role);
        }

        [Fact]
        public void GetRole_ReturnsNull_WhenUserBelongsToDifferentTenant()
        {
            // Arrange
            _tenantRegistry.TryCreate("tenant1", new TenantQuota(), out _, "key1");
            _tenantRegistry.TryCreate("tenant2", new TenantQuota(), out _, "key2");
            _userRegistry.TryCreate("tenant1", "user1", Role.Operator, "user-key", out _);

            // Act
            var role = _authService.GetRole("tenant2", "user-key");

            // Assert
            Assert.Null(role);
        }

        [Fact]
        public void HasPermission_ReturnsFalse_WhenNoRole()
        {
            var result = _authService.HasPermission("tenant1", "unknown-key", Permission.IndexRead);
            Assert.False(result);
        }

        [Fact]
        public void HasPermission_ReturnsTrue_WhenRoleHasPermission()
        {
            // Arrange
            _tenantRegistry.TryCreate("tenant1", new TenantQuota(), out _, "key");
            _userRegistry.TryCreate("tenant1", "user1", Role.Operator, "op-key", out _);

            // Act & Assert
            Assert.True(_authService.HasPermission("tenant1", "op-key", Permission.IndexBuild));
            Assert.True(_authService.HasPermission("tenant1", "op-key", Permission.CacheFlush));
            Assert.False(_authService.HasPermission("tenant1", "op-key", Permission.IndexCreate));
        }

        [Fact]
        public void GetUserId_ReturnsUserId_WhenApiKeyMatches()
        {
            // Arrange
            _tenantRegistry.TryCreate("tenant1", new TenantQuota(), out _, "key");
            _userRegistry.TryCreate("tenant1", "my-user", Role.Reader, "reader-key", out _);

            // Act
            var userId = _authService.GetUserId("tenant1", "reader-key");

            // Assert
            Assert.Equal("my-user", userId);
        }

        [Fact]
        public void GetUserId_ReturnsAdmin_ForLegacyTenantApiKey()
        {
            // Arrange
            _tenantRegistry.TryCreate("tenant1", new TenantQuota(), out _, "legacy-key");

            // Act
            var userId = _authService.GetUserId("tenant1", "legacy-key");

            // Assert
            Assert.Equal("admin", userId);
        }
    }
}
