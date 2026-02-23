#region

using System.Globalization;
using Mehrak.Bot.Modules;
using Mehrak.Bot.Services;
using Mehrak.Bot.Services.RateLimit;
using Mehrak.Domain.Protobuf;
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
using NetCord.Services.ApplicationCommands;
using NetCord.Services.ComponentInteractions;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using Serilog.Extensions;
using Serilog.Sinks.OpenTelemetry;

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


        if (builder.Environment.IsDevelopment() && File.Exists("/.dockerenv"))
        {
            Console.WriteLine("Docker environment detected");
            builder.Configuration.AddJsonFile("appsettings.DockerDev.json");
        }
        else if (builder.Environment.IsDevelopment())
        {
            Console.WriteLine("Development environment detected");
            builder.Configuration.AddJsonFile("appsettings.Development.json");
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
            .WriteTo.OpenTelemetry(options =>
            {
                options.Endpoint = builder.Configuration["Otlp:Endpoint"] ?? "http://localhost:4317";
                options.Protocol = OtlpProtocol.Grpc;
                options.ResourceAttributes = new Dictionary<string, object>
                {
                    ["service.name"] = "MehrakBot",
                    ["deployment.environment"] = builder.Environment.EnvironmentName
                };
            });

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
            builder.Services.Configure<RedisConfig>(builder.Configuration.GetSection("Redis"));
            builder.Services.Configure<PgConfig>(builder.Configuration.GetSection("Postgres"));
            builder.Services.Configure<ClickhouseConfig>(builder.Configuration.GetSection("Clickhouse"));
            builder.Services.Configure<RateLimiterConfig>(builder.Configuration.GetSection("RateLimit"));

            builder.Services.AddInfrastructureServices();
            builder.Services.AddBotServices();
            builder.Services.AddGrpcClient<ApplicationService.ApplicationServiceClient>(options =>
            {
                var address = builder.Configuration["Application:ConnectionString"];
                options.Address = new Uri(address ?? "http://localhost:5000");
            });

            builder.Services.AddHostedService<UserTrackerBackfillService>();

            var otlpEndpoint = new Uri(builder.Configuration["Otlp:Endpoint"] ?? "http://localhost:4317");

            builder.Services.AddOpenTelemetry()
                .ConfigureResource(resource => resource
                    .AddService(serviceName: "MehrakBot", serviceInstanceId: Environment.MachineName))
                .WithTracing(tracing => tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddGrpcClientInstrumentation()
                    .AddSource("MehrakBot")
                    .AddOtlpExporter(o => o.Endpoint = otlpEndpoint))
                .WithMetrics(metrics => metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter("MehrakBot")
                    .AddOtlpExporter((o, m) =>
                    {
                        o.Endpoint = otlpEndpoint;
                        m.TemporalityPreference = MetricReaderTemporalityPreference.Delta;
                    }));

            builder.Services.AddDiscordGateway().AddApplicationCommands(a => a.ResultHandler =
                new CustomCommandResultHandler<ApplicationCommandContext>())
                .AddComponentInteractions<ModalInteraction, ModalInteractionContext>()
                .AddComponentInteractions<ButtonInteraction, ButtonInteractionContext>();

            var host = builder.Build();

            var logger = host.Services.GetRequiredService<ILogger<Program>>();

            host.AddModules(typeof(Program).Assembly);

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
