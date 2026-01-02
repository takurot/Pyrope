using Garnet;
using Garnet.server;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

            // Register Args for GarnetService
            builder.Services.AddSingleton(args);

            // Register GarnetService as Hosted Service
            builder.Services.AddHostedService<GarnetService>();
            builder.Services.AddHostedService<SidecarMetricsReporter>();

            var app = builder.Build();
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
