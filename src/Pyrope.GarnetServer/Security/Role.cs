namespace Pyrope.GarnetServer.Security
{
    /// <summary>
    /// RBAC roles for tenant authorization.
    /// Roles are hierarchical: higher levels include all permissions of lower levels.
    /// </summary>
    public enum Role
    {
        /// <summary>Read-only access: IndexRead, TenantRead, PolicyRead.</summary>
        Reader = 0,

        /// <summary>Operational access: includes Reader + Build, Snapshot, Cache, PolicyUpdate, AuditRead.</summary>
        Operator = 1,

        /// <summary>Full control: includes Operator + Create/Delete, Quotas, User Management.</summary>
        TenantAdmin = 2
    }
}
