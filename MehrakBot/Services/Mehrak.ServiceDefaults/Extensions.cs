using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.OpenTelemetry;

namespace Mehrak.ServiceDefaults;

public static class Extensions
{
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
                options.CircuitBreaker.FailureRatio = 0.3;
            });
            http.AddServiceDiscovery();
        });
        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: builder.Configuration["OTEL_SERVICE_NAME"]
                        ?? builder.Environment.ApplicationName,
                    serviceInstanceId: Environment.MachineName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddGrpcClientInstrumentation();
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            });

        builder.AddOpenTelemetryExporters();
        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        var useOtlp = !string.IsNullOrWhiteSpace(
            builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlp)
        {
            builder.Services.AddOpenTelemetry()
                .UseOtlpExporter();
        }

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        var pgConn = builder.Configuration.GetConnectionString("mehrakdb");
        if (!string.IsNullOrWhiteSpace(pgConn))
            builder.Services.AddHealthChecks().AddNpgSql(pgConn, tags: ["ready"]);

        var redisConn = builder.Configuration.GetConnectionString("redis");
        if (!string.IsNullOrWhiteSpace(redisConn))
            builder.Services.AddHealthChecks().AddRedis(redisConn, tags: ["ready"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health");

        app.MapHealthChecks("/health/ready", new()
        {
            Predicate = check => check.Tags.Contains("ready")
        });

        return app;
    }

    public static TBuilder AddSerilogOtlp<TBuilder>(
        this TBuilder builder,
        string serviceName,
        Action<LoggerConfiguration>? configureLogger = null)
        where TBuilder : IHostApplicationBuilder
    {
        var logLevels = builder.Configuration.GetSection("Logging:LogLevel");
        var defaultLevel = MapLevel(logLevels["Default"], LogEventLevel.Information);

        var loggerConfig = new LoggerConfiguration()
            .MinimumLevel.Is(defaultLevel);

        foreach (var kvp in logLevels.GetChildren().Where(c =>
            !string.Equals(c.Key, "Default", StringComparison.OrdinalIgnoreCase)))
            loggerConfig.MinimumLevel.Override(kvp.Key, MapLevel(kvp.Value, defaultLevel));

        loggerConfig.Enrich.FromLogContext();

        configureLogger?.Invoke(loggerConfig);

        loggerConfig
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
                    ["service.name"] = serviceName,
                    ["deployment.environment"] = builder.Environment.EnvironmentName
                };
            });

        if (builder.Environment.IsDevelopment())
            loggerConfig.MinimumLevel.Debug();

        Log.Logger = loggerConfig.CreateLogger();

        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(dispose: true);

        return builder;
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
