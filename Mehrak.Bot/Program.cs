using Mehrak.Application;
using Mehrak.Bot.Modules;
using Mehrak.Bot.Services;
using Mehrak.GameApi;
using Mehrak.Infrastructure;
using Mehrak.Infrastructure.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services.Commands;
using Serilog;
using Serilog.Sinks.Grafana.Loki;
using System.Globalization;

namespace Mehrak.Bot;

public class Program
{
    public static async Task Main(string[] args)
    {
        HostApplicationBuilderSettings settings = new()
        {
            Args = args,
            Configuration = new ConfigurationManager(),
            ContentRootPath = Directory.GetCurrentDirectory()
        };

        settings.Configuration.AddJsonFile("appsettings.json")
            .AddUserSecrets<Program>()
            .AddEnvironmentVariables()
            .Build();

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        if (builder.Environment.IsDevelopment())
        {
            Console.WriteLine("Development environment detected");
            builder.Configuration.AddJsonFile("appsettings.development.json");
        }

        // Configure Serilog
        LoggerConfiguration loggerConfig = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.Console(
                outputTemplate:
                "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
                formatProvider: CultureInfo.InvariantCulture
            )
            .WriteTo.File(
                "logs/log-.txt",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 31,
                outputTemplate:
                "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
                formatProvider: CultureInfo.InvariantCulture
            )
            .WriteTo.GrafanaLoki(
                builder.Configuration["Loki:ConnectionString"] ?? "http://localhost:3100",
                [
                    new LokiLabel { Key = "app", Value = "MehrakBot" },
                    new LokiLabel { Key = "environment", Value = builder.Environment.EnvironmentName }
                ]);

        if (builder.Environment.IsDevelopment())
            loggerConfig.MinimumLevel.Debug();

        Log.Logger = loggerConfig.CreateLogger();

        try
        {
            Log.Information("Starting MehrakBot application");

            // Configure logging to use Serilog
            builder.Logging.ClearProviders();
            builder.Logging.AddSerilog(dispose: true);

            // Database Services
            builder.Services.Configure<CharacterCacheConfig>(builder.Configuration.GetSection("CharacterCache"));

            // Api Services
            builder.Services.AddHttpClient("Default").ConfigurePrimaryHttpMessageHandler(() =>
                new HttpClientHandler
                {
                    UseCookies = false
                });

            builder.Services.AddInfrastructureServices();

            builder.Services.AddGameApiServices();

            builder.Services.AddGenshinApplicationServices();

            builder.Services.AddHostedService<AsyncInitializationHostedService>();

            var host = builder.Build();

            ILogger<Program> logger = host.Services.GetRequiredService<ILogger<Program>>();

            host.AddCommandModule<GenshinCommandModule>();

            host.UseGatewayHandlers();
            logger.LogInformation("Discord gateway initialized");

            await host.RunAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application start-up failed");
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }
}
