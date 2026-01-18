using Garnet;
using Garnet.server;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pyrope.GarnetServer.Security;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Policies;
using Pyrope.GarnetServer.Services;
using Pyrope.GarnetServer.DataModel;

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

            // --- Billing/Metering (P7) ---
            builder.Services.Configure<BillingOptions>(options =>
            {
                if (double.TryParse(builder.Configuration["Billing:CostUnitSeconds"], out var costSeconds))
                {
                    options.CostUnitSeconds = costSeconds;
                }
                if (int.TryParse(builder.Configuration["Billing:LogIntervalSeconds"], out var interval))
                {
                    options.LogIntervalSeconds = interval;
                }
                if (int.TryParse(builder.Configuration["Billing:MaxInMemoryEntries"], out var maxEntries))
                {
                    options.MaxInMemoryEntries = maxEntries;
                }
                options.LogPath = builder.Configuration["Billing:LogPath"];
            });

            // Register Core Services
            builder.Services.AddSingleton(Pyrope.GarnetServer.Extensions.VectorCommandSet.SharedIndexRegistry);
            builder.Services.AddSingleton<MemoryCacheStorage>();
            builder.Services.AddSingleton<ICacheStorage>(sp => sp.GetRequiredService<MemoryCacheStorage>());
            builder.Services.AddSingleton<ICacheAdmin>(sp => sp.GetRequiredService<MemoryCacheStorage>());
            builder.Services.AddSingleton<ICacheUsageProvider>(sp => sp.GetRequiredService<MemoryCacheStorage>());
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
            builder.Services.AddSingleton<SemanticClusterRegistry>();
            builder.Services.AddSingleton<CanonicalKeyMap>();
            builder.Services.AddSingleton<IBillingLogStore, BillingLogStore>();
            builder.Services.AddSingleton<IBillingMeter, BillingMeter>();

            // --- RBAC (P5-6) ---
            builder.Services.AddSingleton<TenantUserRegistry>();
            builder.Services.AddSingleton<IAuthorizationService, RbacAuthorizationService>();

            // --- Audit Logging (P5-7) ---
            builder.Services.AddSingleton<IAuditLogger>(sp =>
            {
                var logPath = builder.Configuration["Audit:LogPath"];
                var maxEvents = 10000;
                if (int.TryParse(builder.Configuration["Audit:MaxInMemoryEvents"], out var parsed))
                {
                    maxEvents = parsed;
                }
                return new AuditLogger(logPath, maxEvents);
            });

            // Register Args for GarnetService
            builder.Services.AddSingleton(args);

            // Register GarnetService as Hosted Service
            builder.Services.AddHostedService<GarnetService>();
            builder.Services.AddHostedService<SidecarMetricsReporter>();
            builder.Services.AddSingleton<PredictivePrefetcher>();
            builder.Services.AddSingleton<IPredictivePrefetcher>(sp => sp.GetRequiredService<PredictivePrefetcher>());
            builder.Services.AddHostedService(sp => sp.GetRequiredService<PredictivePrefetcher>());
            builder.Services.AddHostedService<SloGuardrailsMonitor>();

            // --- Background Services (P6-5) ---
            builder.Services.AddSingleton<PrefetchBackgroundQueue>();
            builder.Services.AddSingleton<IPrefetchBackgroundQueue>(sp => sp.GetRequiredService<PrefetchBackgroundQueue>());
            builder.Services.AddHostedService(sp => sp.GetRequiredService<PrefetchBackgroundQueue>());

            var app = builder.Build();

            app.UseForwardedHeaders(new ForwardedHeadersOptions
            {
                ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor |
                                 Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
            });

            app.UseRouting();
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
