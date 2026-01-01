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
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers();
            
            // Register Core Services
            builder.Services.AddSingleton(Pyrope.GarnetServer.Extensions.VectorCommandSet.SharedIndexRegistry);
            builder.Services.AddSingleton<MemoryCacheStorage>();
            builder.Services.AddSingleton<ICacheStorage>(sp => sp.GetRequiredService<MemoryCacheStorage>());
            builder.Services.AddSingleton<IMetricsCollector, MetricsCollector>();
            builder.Services.AddSingleton(sp => (MetricsCollector)sp.GetRequiredService<IMetricsCollector>()); // Alias if needed
            builder.Services.AddSingleton<LshService>(_ => new LshService());
            builder.Services.AddSingleton<ResultCache>();
            builder.Services.AddSingleton<IPolicyEngine>(new StaticPolicyEngine(TimeSpan.FromSeconds(60)));
            
            // Register Args for GarnetService
            builder.Services.AddSingleton(args);

            // Register GarnetService as Hosted Service
            builder.Services.AddHostedService<GarnetService>();
            
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
