using Xunit;
using Pyrope.GarnetServer.Security;

namespace Pyrope.GarnetServer.Tests.Security
{
    public class RolePermissionsTests
    {
        [Theory]
        [InlineData(Role.Reader, Permission.IndexRead, true)]
        [InlineData(Role.Reader, Permission.TenantRead, true)]
        [InlineData(Role.Reader, Permission.PolicyRead, true)]
        [InlineData(Role.Reader, Permission.BillingRead, true)]
        [InlineData(Role.Reader, Permission.IndexCreate, false)]
        [InlineData(Role.Reader, Permission.IndexDelete, false)]
        [InlineData(Role.Reader, Permission.IndexBuild, false)]
        [InlineData(Role.Reader, Permission.CacheFlush, false)]
        [InlineData(Role.Reader, Permission.UserManage, false)]
        public void Reader_HasExpectedPermissions(Role role, Permission permission, bool expected)
        {
            Assert.Equal(expected, RolePermissions.HasPermission(role, permission));
        }

        [Theory]
        [InlineData(Role.Operator, Permission.IndexRead, true)]
        [InlineData(Role.Operator, Permission.IndexBuild, true)]
        [InlineData(Role.Operator, Permission.IndexSnapshot, true)]
        [InlineData(Role.Operator, Permission.IndexLoad, true)]
        [InlineData(Role.Operator, Permission.CacheFlush, true)]
        [InlineData(Role.Operator, Permission.CacheInvalidate, true)]
        [InlineData(Role.Operator, Permission.PolicyUpdate, true)]
        [InlineData(Role.Operator, Permission.AuditRead, true)]
        [InlineData(Role.Operator, Permission.BillingRead, true)]
        [InlineData(Role.Operator, Permission.IndexCreate, false)]
        [InlineData(Role.Operator, Permission.IndexDelete, false)]
        [InlineData(Role.Operator, Permission.UserManage, false)]
        public void Operator_HasExpectedPermissions(Role role, Permission permission, bool expected)
        {
            Assert.Equal(expected, RolePermissions.HasPermission(role, permission));
        }

        [Theory]
        [InlineData(Permission.IndexCreate)]
        [InlineData(Permission.IndexDelete)]
        [InlineData(Permission.IndexBuild)]
        [InlineData(Permission.IndexSnapshot)]
        [InlineData(Permission.IndexLoad)]
        [InlineData(Permission.IndexRead)]
        [InlineData(Permission.TenantCreate)]
        [InlineData(Permission.TenantUpdate)]
        [InlineData(Permission.TenantRead)]
        [InlineData(Permission.UserManage)]
        [InlineData(Permission.CacheFlush)]
        [InlineData(Permission.CacheInvalidate)]
        [InlineData(Permission.PolicyUpdate)]
        [InlineData(Permission.PolicyRead)]
        [InlineData(Permission.AuditRead)]
        [InlineData(Permission.BillingRead)]
        public void TenantAdmin_HasAllPermissions(Permission permission)
        {
            Assert.True(RolePermissions.HasPermission(Role.TenantAdmin, permission));
        }

        [Fact]
        public void GetPermissions_ReturnsCorrectCount()
        {
            var readerPerms = RolePermissions.GetPermissions(Role.Reader);
            var operatorPerms = RolePermissions.GetPermissions(Role.Operator);
            var adminPerms = RolePermissions.GetPermissions(Role.TenantAdmin);

            Assert.Equal(4, readerPerms.Count);
            Assert.True(operatorPerms.Count > readerPerms.Count);
            Assert.True(adminPerms.Count >= operatorPerms.Count);
        }
    }
}
