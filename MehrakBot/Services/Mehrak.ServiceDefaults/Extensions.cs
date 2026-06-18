using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

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
                    .AddHttpClientInstrumentation();

                if (builder.Services.Any(sd =>
                        sd.ServiceType.FullName?.Contains("GrpcClient") == true))
                {
                    tracing.AddGrpcClientInstrumentation();
                }
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
            builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"])
            || !string.IsNullOrWhiteSpace(
                builder.Configuration["Otlp:Endpoint"]);

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
}
