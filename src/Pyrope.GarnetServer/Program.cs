using Garnet;
using Garnet.server;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pyrope.GarnetServer.Security;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Policies;
using Pyrope.GarnetServer.Services;

namespace Pyrope.GarnetServer
{
    public class Program
    {
        public static void Main(string[] args)
        {
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers();

            // --- Security (P5-5) ---
            // Enabled by default. Configure AdminApiKey via:
            // - Auth:AdminApiKey (preferred)
            // - PYROPE_ADMIN_API_KEY (env var)
            builder.Services.Configure<ApiKeyAuthOptions>(options =>
            {
                options.Enabled = !string.Equals(builder.Configuration["Auth:Enabled"], "false", StringComparison.OrdinalIgnoreCase);
                options.AdminApiKey =
                    builder.Configuration["Auth:AdminApiKey"] ??
                    builder.Configuration["PYROPE_ADMIN_API_KEY"] ??
                    Environment.GetEnvironmentVariable("PYROPE_ADMIN_API_KEY") ??
                    "";
            });

            // --- SLO Guardrails (P5-4) ---
            builder.Services.Configure<SloGuardrailsOptions>(options =>
            {
                if (bool.TryParse(builder.Configuration["Slo:Enabled"], out var enabled))
                {
                    options.Enabled = enabled;
                }
                if (double.TryParse(builder.Configuration["Slo:TargetP99Ms"], out var p99))
                {
                    options.TargetP99Ms = p99;
                }
                if (double.TryParse(builder.Configuration["Slo:RecoveryFactor"], out var recovery))
                {
                    options.RecoveryFactor = recovery;
                }
                if (int.TryParse(builder.Configuration["Slo:DegradedMaxScans"], out var scans))
                {
                    options.DegradedMaxScans = scans;
                }
                if (int.TryParse(builder.Configuration["Slo:MonitorIntervalSeconds"], out var intervalSec))
                {
                    options.MonitorIntervalSeconds = intervalSec;
                }
                if (int.TryParse(builder.Configuration["Slo:MinSamplesPerInterval"], out var minSamples))
                {
                    options.MinSamplesPerInterval = minSamples;
                }
            });

            // Register Core Services
            builder.Services.AddSingleton(Pyrope.GarnetServer.Extensions.VectorCommandSet.SharedIndexRegistry);
            builder.Services.AddSingleton<MemoryCacheStorage>();
            builder.Services.AddSingleton<ICacheStorage>(sp => sp.GetRequiredService<MemoryCacheStorage>());
            builder.Services.AddSingleton<ICacheAdmin>(sp => sp.GetRequiredService<MemoryCacheStorage>());
            builder.Services.AddSingleton<IMetricsCollector, MetricsCollector>();
            builder.Services.AddSingleton(sp => (MetricsCollector)sp.GetRequiredService<IMetricsCollector>()); // Alias if needed
            builder.Services.AddSingleton<ISystemUsageProvider, SystemUsageProvider>();
            builder.Services.AddSingleton<ITimeProvider, SystemTimeProvider>();
            builder.Services.AddSingleton<LshService>(_ => new LshService());
            builder.Services.AddSingleton<ResultCache>();
            builder.Services.AddSingleton<CachePolicyStore>();
            builder.Services.AddSingleton<DynamicPolicyEngine>();
            builder.Services.AddSingleton<IPolicyEngine>(sp => sp.GetRequiredService<DynamicPolicyEngine>());
            builder.Services.AddSingleton<TenantRegistry>();
            builder.Services.AddSingleton<ITenantQuotaEnforcer, TenantQuotaEnforcer>();
            builder.Services.AddSingleton<ITenantAuthenticator, TenantApiKeyAuthenticator>();
            builder.Services.AddSingleton<ISloGuardrails, SloGuardrails>();

            // Register Args for GarnetService
            builder.Services.AddSingleton(args);

            // Register GarnetService as Hosted Service
            builder.Services.AddHostedService<GarnetService>();
            builder.Services.AddHostedService<SidecarMetricsReporter>();
            builder.Services.AddHostedService<SloGuardrailsMonitor>();

            var app = builder.Build();
            app.UseMiddleware<ApiKeyAuthMiddleware>();
            app.MapControllers();

            try
            {
                // Run ASP.NET Core (blocking)
                app.Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unable to start Application: {ex.Message}");
            }
        }
    }
}
