using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

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
            var tenantId = context.RouteData.Values["tenantId"]?.ToString();

            if (string.IsNullOrEmpty(apiKey))
            {
                context.Result = new UnauthorizedResult();
                return;
            }

            // If tenantId is not in route, check query string
            if (string.IsNullOrEmpty(tenantId))
            {
                tenantId = context.HttpContext.Request.Query["tenantId"].ToString();
            }

            // Note: If tenantId is still missing, it depends on the action. 
            // If it's a global action, authService needs to handle it.
            // For now, most actions have a tenantId.
            
            if (string.IsNullOrEmpty(tenantId))
            {
                // Search in body for certain requests (e.g. CreateIndex)
                // But generally we prefer route parameters.
            }

            if (!authService.HasPermission(tenantId ?? "system", apiKey, _permission))
            {
                context.Result = new ForbidResult();
                return;
            }

            // Store UserId and Role in Items for subsequent use (e.g. Audit Logging)
            var userId = authService.GetUserId(tenantId ?? "system", apiKey);
            var role = authService.GetRole(tenantId ?? "system", apiKey);
            
            if (userId != null) context.HttpContext.Items["PyropeUserId"] = userId;
            if (role != null) context.HttpContext.Items["PyropeUserRole"] = role.ToString();

            await next();
        }
    }
}
