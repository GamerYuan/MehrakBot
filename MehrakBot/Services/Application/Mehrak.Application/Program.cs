using System.Globalization;
using Mehrak.Application;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Models;
using Mehrak.Application.Shared.Services;
using Mehrak.GameApi;
using Mehrak.Infrastructure;
using Mehrak.Infrastructure.Shared.Config;
using Mehrak.ServiceDefaults;
using OpenTelemetry.Metrics;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.OpenTelemetry;
using Proto = Mehrak.Domain.Protobuf;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.AddServiceDefaults();
        builder.Services.AddOpenTelemetry().WithMetrics(m => m.AddMeter("MehrakApplication"));

        if (builder.Environment.IsDevelopment())
        {
            Console.WriteLine("Development environment detected");
        }

        var logLevels = builder.Configuration.GetSection("Logging:LogLevel");
        var defaultLevel = MapLevel(logLevels["Default"], LogEventLevel.Information);

        var loggerConfig = new LoggerConfiguration()
            .MinimumLevel.Is(defaultLevel);

        foreach (var kvp in logLevels.GetChildren().Where(c => !string.Equals(c.Key, "Default", StringComparison.OrdinalIgnoreCase)))
            loggerConfig.MinimumLevel.Override(kvp.Key, MapLevel(kvp.Value, defaultLevel));

        // Configure Serilog
        loggerConfig
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
            .WriteTo.OpenTelemetry(options =>
            {
                options.Endpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
                    ?? "http://localhost:4317";
                options.Protocol = OtlpProtocol.Grpc;
                options.ResourceAttributes = new Dictionary<string, object>
                {
                    ["service.name"] = "MehrakApplication",
                    ["deployment.environment"] = builder.Environment.EnvironmentName
                };
            });

        if (builder.Environment.IsDevelopment())
            loggerConfig.MinimumLevel.Debug();

        Log.Logger = loggerConfig.CreateLogger();

        Log.Information("Starting Mehrak Application Service");

        builder.Services.Configure<S3StorageConfig>(builder.Configuration.GetSection("Storage"));
        builder.Services.Configure<RedisConfig>(options =>
        {
            options.ConnectionString = builder.Configuration.GetConnectionString("redis") ?? options.ConnectionString;
            options.InstanceName = builder.Configuration.GetValue<string>("Redis:InstanceName") ?? "Mehrak_";
        });
        builder.Services.Configure<PgConfig>(options =>
            options.ConnectionString = builder.Configuration.GetConnectionString("mehrakdb") ?? options.ConnectionString);
        builder.Services.Configure<CommandDispatcherConfig>(builder.Configuration.GetSection("CommandDispatcher"));

        builder.Host.UseSerilog();

        builder.Services.AddHttpClient("Default").ConfigurePrimaryHttpMessageHandler(() =>
            new HttpClientHandler
            {
                UseCookies = false
            }).ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(30));

        builder.Services.AddHostedService<AssetInitializationService>();

        // Add services to the container.
        builder.Services.AddGrpc();
        builder.Services.AddGameApiServices();
        builder.Services.AddInfrastructureServices();
        builder.Services.AddApplicationServices();

        builder.Services.AddGrpcClient<Proto.ImageProcessorService.ImageProcessorServiceClient>(options =>
        {
            var address = builder.Configuration.GetConnectionString("image-processor") ?? "http://image-processor";
            options.Address = new Uri(address);
        });

        builder.Services.AddSingleton<CommandDispatcher>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<CommandDispatcher>());

        builder.Services.AddSingleton<IApplicationMetrics, ApplicationMetricsService>();

        var app = builder.Build();

        app.MapDefaultEndpoints();

        // Configure the HTTP request pipeline.
        app.MapGrpcService<GrpcApplicationService>();
        app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client.");

        await app.RunAsync();
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
