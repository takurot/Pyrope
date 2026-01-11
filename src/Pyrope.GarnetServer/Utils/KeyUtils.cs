namespace Pyrope.GarnetServer.Utils
{
    public static class KeyUtils
    {
        public static string GetIndexConfigKey(string tenantId, string indexName)
        {
            TenantNamespace.ValidateTenantId(tenantId);
            TenantNamespace.ValidateIndexName(indexName);
            return $"_meta:tenant:{tenantId}:index:{indexName}:config";
        }

        public static string GetTenantConfigKey(string tenantId)
        {
            TenantNamespace.ValidateTenantId(tenantId);
            return $"_meta:tenant:{tenantId}:config";
        }

        public static string GetCacheKeyPrefix(string tenantId, string indexName)
        {
            TenantNamespace.ValidateTenantId(tenantId);
            TenantNamespace.ValidateIndexName(indexName);
            return $"cache:{tenantId}:{indexName}:";
        }

        public static string GetIndexKey(string tenantId, string indexName)
        {
            TenantNamespace.ValidateTenantId(tenantId);
            TenantNamespace.ValidateIndexName(indexName);
            return $"idx:{tenantId}:{indexName}";
        }
    }
}
