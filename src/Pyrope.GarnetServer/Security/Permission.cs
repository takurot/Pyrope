using System;
using System.Collections.Generic;

namespace Pyrope.GarnetServer.Security
{
    /// <summary>
    /// Permissions for RBAC authorization.
    /// </summary>
    public enum Permission
    {
        // Index operations
        IndexCreate,
        IndexDelete,
        IndexBuild,
        IndexSnapshot,
        IndexLoad,
        IndexRead,

        // Tenant operations
        TenantCreate,
        TenantUpdate,
        TenantRead,
        UserManage,

        // Cache operations
        CacheFlush,
        CacheInvalidate,
        PolicyUpdate,
        PolicyRead,

        // Audit operations
        AuditRead
    }

    /// <summary>
    /// Maps roles to their allowed permissions.
    /// </summary>
    public static class RolePermissions
    {
        private static readonly Dictionary<Role, HashSet<Permission>> _permissions;

        static RolePermissions()
        {
            var readerPerms = new HashSet<Permission>
            {
                Permission.IndexRead,
                Permission.TenantRead,
                Permission.PolicyRead
            };

            var operatorPerms = new HashSet<Permission>(readerPerms)
            {
                Permission.IndexBuild,
                Permission.IndexSnapshot,
                Permission.IndexLoad,
                Permission.CacheFlush,
                Permission.CacheInvalidate,
                Permission.PolicyUpdate,
                Permission.AuditRead
            };

            var adminPerms = new HashSet<Permission>(operatorPerms)
            {
                Permission.IndexCreate,
                Permission.IndexDelete,
                Permission.TenantCreate,
                Permission.TenantUpdate,
                Permission.UserManage
            };

            _permissions = new()
            {
                [Role.Reader] = readerPerms,
                [Role.Operator] = operatorPerms,
                [Role.TenantAdmin] = adminPerms
            };
        }

        /// <summary>
        /// Checks if a role has a specific permission.
        /// </summary>
        public static bool HasPermission(Role role, Permission permission)
        {
            return _permissions.TryGetValue(role, out var perms) && perms.Contains(permission);
        }

        /// <summary>
        /// Gets all permissions for a role.
        /// </summary>
        public static IReadOnlySet<Permission> GetPermissions(Role role)
        {
            return _permissions.TryGetValue(role, out var perms) ? perms : new HashSet<Permission>();
        }
    }
}
