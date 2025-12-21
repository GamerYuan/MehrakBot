#region

using System.Globalization;
using Mehrak.Application;
using Mehrak.Bot.Services;
using Mehrak.Domain.Repositories;
using Mehrak.GameApi;
using Mehrak.Infrastructure;
using Mehrak.Infrastructure.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services;
using NetCord.Hosting.Services.ApplicationCommands;
using NetCord.Hosting.Services.ComponentInteractions;
using NetCord.Services.ComponentInteractions;
using Serilog;
using Serilog.Events;
using Serilog.Extensions;
using Serilog.Sinks.Grafana.Loki;

#endregion

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

        var builder = Host.CreateApplicationBuilder(args);

        if (builder.Environment.IsDevelopment())
        {
            Console.WriteLine("Development environment detected");
            builder.Configuration.AddJsonFile("appsettings.development.json");
        }

        var logLevels = builder.Configuration.GetSection("Logging:LogLevel");
        var defaultLevel = MapLevel(logLevels["Default"], LogEventLevel.Information);

        var loggerConfig = new LoggerConfiguration()
            .MinimumLevel.Is(defaultLevel);

        // apply overrides from config (System, Microsoft, EF, etc.)
        foreach (var kvp in logLevels.GetChildren().Where(c => !string.Equals(c.Key, "Default", StringComparison.OrdinalIgnoreCase)))
            loggerConfig.MinimumLevel.Override(kvp.Key, MapLevel(kvp.Value, defaultLevel));

        // Configure Serilog
        loggerConfig
            .Enrich.FromLogContext()
            .Enrich.WithRequestQuery()
            .Enrich.WithRequestBody()
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
            builder.Services.Configure<S3StorageConfig>(builder.Configuration.GetSection("Storage"));

            // Api Services
            builder.Services.AddHttpClient("Default").ConfigurePrimaryHttpMessageHandler(() =>
                new HttpClientHandler
                {
                    UseCookies = false
                }).ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(30));

            builder.Services.AddInfrastructureServices(builder.Configuration);

            builder.Services.AddGameApiServices();

            builder.Services.AddApplicationServices();

            builder.Services.AddBotServices();

            builder.Services.AddDiscordGateway().AddApplicationCommands()
                .AddComponentInteractions<ModalInteraction, ModalInteractionContext>()
                .AddComponentInteractions<ButtonInteraction, ButtonInteractionContext>();

            var host = builder.Build();

            var logger = host.Services.GetRequiredService<ILogger<Program>>();

            host.AddModules(typeof(Program).Assembly);

            host.UseGatewayHandlers();
            logger.LogInformation("Discord gateway initialized");

            var imageRepo = host.Services.GetRequiredService<IImageRepository>();

            foreach (var image in Directory.EnumerateFiles($"{AppContext.BaseDirectory}Assets", "*.png",
                         SearchOption.AllDirectories))
            {
                if (Path.GetDirectoryName(image)?.Contains("Test") ?? false) continue;
                var fileName = Path.GetFileName(image).Split('.')[0];
                if (await imageRepo.FileExistsAsync(fileName)) continue;

                await using var stream = File.OpenRead(image);
                await imageRepo.UploadFileAsync(fileName, stream);
                logger.LogInformation("Uploaded {FileName} to Image Repository, file path {Image}", fileName, image);
            }

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

    private static LogEventLevel MapLevel(string? value, LogEventLevel fallback) =>
        value?.ToLowerInvariant() switch
        {
            "trace" or "verbose" => LogEventLevel.Verbose,
            "debug" => LogEventLevel.Debug,
            "information" or "info" => LogEventLevel.Information,
            "warning" or "warn" => LogEventLevel.Warning,
            "error" => LogEventLevel.Error,
            "critical" or "fatal" => LogEventLevel.Fatal,
            "none" => LogEventLevel.Fatal + 1,
            _ => fallback
        };
}
