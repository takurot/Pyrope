using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Pyrope.GarnetServer.Utils;

namespace Pyrope.GarnetServer.Security
{
    /// <summary>
    /// Declarative permission check for Controller actions.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = true, AllowMultiple = true)]
    public sealed class RequirePermissionAttribute : Attribute, IFilterFactory
    {
        public Permission Permission { get; }
        public bool IsReusable => true;

        public RequirePermissionAttribute(Permission permission)
        {
            Permission = permission;
        }

        public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
        {
            return new PermissionFilter(Permission);
        }
    }

    /// <summary>
    /// Filter that checks for a specific permission using IAuthorizationService.
    /// </summary>
    public sealed class PermissionFilter : IAsyncActionFilter
    {
        private readonly Permission _permission;

        public PermissionFilter(Permission permission)
        {
            _permission = permission;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            // 1. Check if it's a global admin (assigned by ApiKeyAuthMiddleware)
            if (context.HttpContext.Items.TryGetValue("IsAdmin", out var isAdminObj) && isAdminObj is bool isAdmin && isAdmin)
            {
                await next();
                return;
            }

            var authService = context.HttpContext.RequestServices.GetRequiredService<IAuthorizationService>();

            // API Key should be injected into HttpContext.Items by ApiKeyAuthMiddleware
            var apiKey = context.HttpContext.Items["PyropeApiKey"]?.ToString();
            if (string.IsNullOrEmpty(apiKey))
            {
                context.Result = new UnauthorizedResult();
                return;
            }

            // Extract tenantId strictly from route, query, or body ActionArguments
            var tenantId = context.RouteData.Values["tenantId"]?.ToString();
            if (string.IsNullOrEmpty(tenantId))
            {
                tenantId = context.RouteData.Values["id"]?.ToString();
            }

            if (string.IsNullOrEmpty(tenantId) && context.HttpContext.Request.Query.TryGetValue("tenantId", out var queryVal))
            {
                tenantId = queryVal.ToString();
            }

            if (string.IsNullOrEmpty(tenantId) && context.ActionArguments != null)
            {
                foreach (var arg in context.ActionArguments.Values)
                {
                    if (arg == null) continue;
                    var prop = arg.GetType().GetProperty("TenantId", BindingFlags.Public | BindingFlags.Instance);
                    if (prop != null && prop.PropertyType == typeof(string))
                    {
                        var val = prop.GetValue(arg) as string;
                        if (!string.IsNullOrWhiteSpace(val))
                        {
                            tenantId = val;
                            break;
                        }
                    }
                }
            }

            // For non-global-admin callers, fail closed if tenantId is missing
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                context.Result = new ForbidResult();
                return;
            }

            // Validate tenantId format
            if (!TenantNamespace.TryValidateTenantId(tenantId, out _))
            {
                context.Result = new ForbidResult();
                return;
            }

            // Cross-tenant guard: if middleware authenticated a specific tenant, ensure it matches requested tenantId
            var authenticatedTenantId = context.HttpContext.Items["PyropeTenantId"]?.ToString();
            if (!string.IsNullOrEmpty(authenticatedTenantId) && !string.Equals(authenticatedTenantId, tenantId, StringComparison.Ordinal))
            {
                context.Result = new ForbidResult();
                return;
            }

            if (!authService.HasPermission(tenantId, apiKey, _permission))
            {
                context.Result = new ForbidResult();
                return;
            }

            // Store UserId and Role in Items for subsequent use (e.g. Audit Logging)
            var userId = authService.GetUserId(tenantId, apiKey);
            var role = authService.GetRole(tenantId, apiKey);

            if (userId != null) context.HttpContext.Items["PyropeUserId"] = userId;
            if (role != null) context.HttpContext.Items["PyropeUserRole"] = role.ToString();

            await next();
        }
    }
}
