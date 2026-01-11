using Xunit;
using Pyrope.GarnetServer.Security;
using Pyrope.GarnetServer.Services;

namespace Pyrope.GarnetServer.Tests.Services
{
    public class TenantUserRegistryTests
    {
        private readonly TenantUserRegistry _registry;

        public TenantUserRegistryTests()
        {
            _registry = new TenantUserRegistry();
        }

        [Fact]
        public void TryCreate_CreatesUser_ReturnsTrue()
        {
            // Act
            var result = _registry.TryCreate("tenant1", "user1", Role.Operator, "api-key-1", out var user);

            // Assert
            Assert.True(result);
            Assert.NotNull(user);
            Assert.Equal("user1", user.UserId);
            Assert.Equal("tenant1", user.TenantId);
            Assert.Equal(Role.Operator, user.Role);
            Assert.Equal("api-key-1", user.ApiKey);
        }

        [Fact]
        public void TryCreate_ReturnsFalse_WhenUserExists()
        {
            // Arrange
            _registry.TryCreate("tenant1", "user1", Role.Reader, "key1", out _);

            // Act
            var result = _registry.TryCreate("tenant1", "user1", Role.Operator, "key2", out var user);

            // Assert
            Assert.False(result);
            Assert.Null(user);
        }

        [Fact]
        public void TryGet_ReturnsUser_WhenExists()
        {
            // Arrange
            _registry.TryCreate("tenant1", "user1", Role.TenantAdmin, "key1", out _);

            // Act
            var result = _registry.TryGet("tenant1", "user1", out var user);

            // Assert
            Assert.True(result);
            Assert.NotNull(user);
            Assert.Equal("user1", user.UserId);
        }

        [Fact]
        public void TryGet_ReturnsFalse_WhenNotExists()
        {
            var result = _registry.TryGet("tenant1", "unknown", out var user);
            Assert.False(result);
            Assert.Null(user);
        }

        [Fact]
        public void TryGetByApiKey_ReturnsUser_WhenKeyMatches()
        {
            // Arrange
            _registry.TryCreate("tenant1", "user1", Role.Reader, "my-api-key", out _);

            // Act
            var result = _registry.TryGetByApiKey("my-api-key", out var user);

            // Assert
            Assert.True(result);
            Assert.NotNull(user);
            Assert.Equal("user1", user.UserId);
        }

        [Fact]
        public void TryGetByApiKey_ReturnsFalse_WhenKeyNotFound()
        {
            var result = _registry.TryGetByApiKey("unknown-key", out var user);
            Assert.False(result);
            Assert.Null(user);
        }

        [Fact]
        public void GetByTenant_ReturnsAllUsersForTenant()
        {
            // Arrange
            _registry.TryCreate("tenant1", "user1", Role.Reader, "key1", out _);
            _registry.TryCreate("tenant1", "user2", Role.Operator, "key2", out _);
            _registry.TryCreate("tenant2", "user3", Role.TenantAdmin, "key3", out _);

            // Act
            var users = _registry.GetByTenant("tenant1").ToList();

            // Assert
            Assert.Equal(2, users.Count);
            Assert.Contains(users, u => u.UserId == "user1");
            Assert.Contains(users, u => u.UserId == "user2");
        }

        [Fact]
        public void TryUpdateRole_UpdatesRole_WhenUserExists()
        {
            // Arrange
            _registry.TryCreate("tenant1", "user1", Role.Reader, "key1", out _);

            // Act
            var result = _registry.TryUpdateRole("tenant1", "user1", Role.TenantAdmin, out var user);

            // Assert
            Assert.True(result);
            Assert.NotNull(user);
            Assert.Equal(Role.TenantAdmin, user.Role);
        }

        [Fact]
        public void TryDelete_RemovesUser_WhenExists()
        {
            // Arrange
            _registry.TryCreate("tenant1", "user1", Role.Reader, "key1", out _);

            // Act
            var result = _registry.TryDelete("tenant1", "user1", out var user);

            // Assert
            Assert.True(result);
            Assert.NotNull(user);
            Assert.False(_registry.TryGet("tenant1", "user1", out _));
            Assert.False(_registry.TryGetByApiKey("key1", out _));
        }

        [Fact]
        public void TryCreate_DuplicateApiKey_AcrossTenants_Fails()
        {
            var tenant1 = "tenant1";
            var tenant2 = "tenant2";
            var apiKey = "shared-key";

            // First user
            Assert.True(_registry.TryCreate(tenant1, "user1", Role.Reader, apiKey, out _));

            // Second user with same key in different tenant
            Assert.False(_registry.TryCreate(tenant2, "user2", Role.Reader, apiKey, out var user2));
            Assert.Null(user2);
            
            // Verify user1 still exists and user2 doesn't
            Assert.True(_registry.TryGetByApiKey(apiKey, out var found));
            Assert.Equal("user1", found!.UserId);
        }

        [Fact]
        public void TryCreate_DuplicateUserId_InSameTenant_FailsButKeepsIndex()
        {
            var tenant = "tenant";
            var key1 = "key1";
            var key2 = "key2";

            Assert.True(_registry.TryCreate(tenant, "user1", Role.Reader, key1, out _));
            
            // Same userId, different apiKey
            Assert.False(_registry.TryCreate(tenant, "user1", Role.Operator, key2, out _));
            
            // key2 should NOT be in index because user creation failed
            Assert.False(_registry.TryGetByApiKey(key2, out _));
            // key1 should still be in index
            Assert.True(_registry.TryGetByApiKey(key1, out var found));
            Assert.Equal("user1", found!.UserId);
        }
    }
}
