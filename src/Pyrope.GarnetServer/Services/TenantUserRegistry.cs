using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Security;
using Pyrope.GarnetServer.Utils;

namespace Pyrope.GarnetServer.Services
{
    /// <summary>
    /// Registry for managing tenant users with RBAC roles.
    /// Thread-safe in-memory store.
    /// </summary>
    public class TenantUserRegistry
    {
        // Key: tenantId:userId
        private readonly ConcurrentDictionary<string, TenantUser> _users = new(StringComparer.Ordinal);
        // Index: apiKey -> tenantId:userId (for fast lookup by API key)
        private readonly ConcurrentDictionary<string, string> _apiKeyIndex = new(StringComparer.Ordinal);

        private static string GetKey(string tenantId, string userId) => $"{tenantId}:{userId}";

        /// <summary>
        /// Creates a new user for a tenant.
        /// </summary>
        public bool TryCreate(string tenantId, string userId, Role role, string apiKey, out TenantUser? user)
        {
            TenantNamespace.ValidateTenantId(tenantId);
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(apiKey))
            {
                user = null;
                return false;
            }

            var key = GetKey(tenantId, userId);
            
            // Enforce global API key uniqueness
            if (!_apiKeyIndex.TryAdd(apiKey, key))
            {
                user = null;
                return false;
            }

            var now = DateTimeOffset.UtcNow;
            var newUser = new TenantUser(userId, tenantId, role, apiKey, now);

            if (_users.TryAdd(key, newUser))
            {
                user = newUser;
                return true;
            }

            // Rollback index if user ID already exists for this tenant
            _apiKeyIndex.TryRemove(apiKey, out _);
            user = null;
            return false;
        }

        /// <summary>
        /// Gets a user by tenant ID and user ID.
        /// </summary>
        public bool TryGet(string tenantId, string userId, out TenantUser? user)
        {
            var key = GetKey(tenantId, userId);
            return _users.TryGetValue(key, out user);
        }

        /// <summary>
        /// Gets a user by API key.
        /// </summary>
        public bool TryGetByApiKey(string apiKey, out TenantUser? user)
        {
            user = null;
            if (string.IsNullOrWhiteSpace(apiKey))
                return false;

            if (_apiKeyIndex.TryGetValue(apiKey, out var key))
            {
                return _users.TryGetValue(key, out user);
            }
            return false;
        }

        /// <summary>
        /// Gets all users for a tenant.
        /// </summary>
        public IEnumerable<TenantUser> GetByTenant(string tenantId)
        {
            TenantNamespace.ValidateTenantId(tenantId);
            var prefix = $"{tenantId}:";
            return _users.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
                         .Select(kv => kv.Value);
        }

        /// <summary>
        /// Updates a user's role.
        /// </summary>
        public bool TryUpdateRole(string tenantId, string userId, Role role, out TenantUser? user)
        {
            var key = GetKey(tenantId, userId);
            if (_users.TryGetValue(key, out user))
            {
                user.UpdateRole(role, DateTimeOffset.UtcNow);
                return true;
            }
            user = null;
            return false;
        }

        /// <summary>
        /// Deletes a user.
        /// </summary>
        public bool TryDelete(string tenantId, string userId, out TenantUser? user)
        {
            var key = GetKey(tenantId, userId);
            if (_users.TryRemove(key, out user))
            {
                _apiKeyIndex.TryRemove(user.ApiKey, out _);
                return true;
            }
            user = null;
            return false;
        }
    }
}
