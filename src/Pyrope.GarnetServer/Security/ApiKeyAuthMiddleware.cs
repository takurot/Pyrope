using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Pyrope.GarnetServer.Services;

namespace Pyrope.GarnetServer.Security
{
    public sealed class ApiKeyAuthMiddleware
    {
        private const string HeaderName = "X-API-KEY";
        private readonly RequestDelegate _next;
        private readonly ApiKeyAuthOptions _options;

        public ApiKeyAuthMiddleware(RequestDelegate next, IOptions<ApiKeyAuthOptions> options)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        }

        public async Task InvokeAsync(HttpContext context, TenantUserRegistry userRegistry, TenantRegistry tenantRegistry)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (!_options.Enabled)
            {
                await _next(context);
                return;
            }

            // Protect control plane endpoints
            var path = context.Request.Path;
            if (!path.HasValue || !path.Value!.StartsWith("/v1/", StringComparison.Ordinal))
            {
                await _next(context);
                return;
            }

            if (string.IsNullOrWhiteSpace(_options.AdminApiKey))
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsync("Server auth misconfigured: missing admin API key.");
                return;
            }

            if (!context.Request.Headers.TryGetValue(HeaderName, out var apiKeyValues))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Missing API key.");
                return;
            }

            var apiKey = apiKeyValues.ToString();

            // 1. Check Admin API Key
            if (string.Equals(apiKey, _options.AdminApiKey, StringComparison.Ordinal))
            {
                context.Items["PyropeApiKey"] = apiKey;
                context.Items["PyropeUserId"] = "admin";
                context.Items["IsAdmin"] = true;
                await _next(context);
                return;
            }

            // 2. Check per-user API Key
            if (userRegistry.TryGetByApiKey(apiKey, out var user))
            {
                context.Items["PyropeApiKey"] = apiKey;
                context.Items["PyropeUserId"] = user!.UserId;
                context.Items["PyropeTenantId"] = user!.TenantId;
                context.Items["PyropeUserRole"] = user!.Role.ToString();
                context.Items["IsAdmin"] = false;
                await _next(context);
                return;
            }

            // 3. Check legacy tenant API Key
            if (tenantRegistry.TryGetByApiKey(apiKey, out var tenant))
            {
                context.Items["PyropeApiKey"] = apiKey;
                context.Items["PyropeUserId"] = "admin"; // Treated as admin of the tenant
                context.Items["PyropeTenantId"] = tenant!.TenantId;
                context.Items["PyropeUserRole"] = Role.TenantAdmin.ToString();
                context.Items["IsAdmin"] = false;
                await _next(context);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Invalid API key.");
        }
    }
}

