using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

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

        public async Task InvokeAsync(HttpContext context)
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
            if (!string.Equals(apiKey, _options.AdminApiKey, StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Invalid API key.");
                return;
            }

            await _next(context);
        }
    }
}

