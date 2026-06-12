using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Mehrak.Dashboard.ReleaseNote;
using Mehrak.Dashboard.Shared.Auth;
using Mehrak.Dashboard.Shared.Services;
using Mehrak.Domain.Auth;
using Mehrak.Domain.Protobuf;
using Mehrak.Infrastructure;
using Mehrak.Infrastructure.Auth;
using Mehrak.Infrastructure.Auth.Entities;
using Mehrak.Infrastructure.Auth.Services;
using Mehrak.Infrastructure.Shared.Config;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Client;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.OpenTelemetry;
using Yarp.ReverseProxy.Configuration;

namespace Mehrak.Dashboard;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

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
                options.Endpoint = builder.Configuration["Otlp:Endpoint"] ?? "http://localhost:4317";
                options.Protocol = OtlpProtocol.Grpc;
                options.ResourceAttributes = new Dictionary<string, object>
                {
                    ["service.name"] = "MehrakDashboard",
                    ["deployment.environment"] = builder.Environment.EnvironmentName
                };
            });

        if (builder.Environment.IsDevelopment())
            loggerConfig.MinimumLevel.Debug();

        Log.Logger = loggerConfig.CreateLogger();

        Log.Information("Starting Mehrak Dashboard");

        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(dispose: true);

        builder.Services.Configure<S3StorageConfig>(builder.Configuration.GetSection("Storage"));
        builder.Services.Configure<UserPortraitStorageConfig>(builder.Configuration.GetSection("UserPortraitStorage"));

        builder.Services.Configure<RedisConfig>(builder.Configuration.GetSection("Redis"));
        builder.Services.Configure<PgConfig>(builder.Configuration.GetSection("Postgres"));

        // Auth services
        builder.Services.AddScoped<IDashboardAuthService, DashboardAuthService>();
        builder.Services.AddScoped<IDashboardUserService, DashboardUserService>();

        builder.Services.AddScoped<IDashboardProfileAuthenticationService, DashboardProfileAuthenticationService>();
        builder.Services.AddScoped<DashboardCookieEvents>();

        builder.Services.AddInfrastructureServices();

        builder.Services.AddHttpClient("Default").ConfigurePrimaryHttpMessageHandler(() =>
            new HttpClientHandler
            {
                UseCookies = false
            }).ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(30));

        var seaweedFilerBaseUrl = NormalizeAbsoluteUrl(
            builder.Configuration["SeaweedFiler:BaseUrl"] ??
                throw new ArgumentException("SeaweedFiler:BaseUrl cannot be empty."),
            "SeaweedFiler:BaseUrl");

        builder.Services.AddReverseProxy().LoadFromMemory(
            [
                new RouteConfig
                {
                    RouteId = "seaweed-filer-prefixed",
                    ClusterId = "seaweed-filer",
                    AuthorizationPolicy = "RequireSuperAdmin",
                    Match = new RouteMatch
                    {
                        Path = "/admin/seaweed-filer/{**catch-all}"
                    },
                    Transforms =
                    [
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["PathRemovePrefix"] = "/admin/seaweed-filer"
                        },
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["RequestHeaderRemove"] = "Authorization"
                        },
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["RequestHeaderRemove"] = "Cookie"
                        },
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["ResponseHeaderRemove"] = "Set-Cookie"
                        }
                    ]
                },
                new RouteConfig
                {
                    RouteId = "seaweed-filer-fallback",
                    ClusterId = "seaweed-filer",
                    AuthorizationPolicy = "RequireSuperAdmin",
                    Order = 10000,
                    Match = new RouteMatch
                    {
                        Path = "/{**catch-all}"
                    },
                    Transforms =
                    [
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["PathPattern"] = "/{**catch-all}"
                        },
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["RequestHeaderRemove"] = "Authorization"
                        },
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["RequestHeaderRemove"] = "Cookie"
                        },
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["ResponseHeaderRemove"] = "Set-Cookie"
                        }
                    ]
                }
            ],
            [
                new ClusterConfig
                {
                    ClusterId = "seaweed-filer",
                    Destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["default"] = new DestinationConfig
                        {
                            Address = seaweedFilerBaseUrl
                        }
                    }
                }
            ]);

        builder.Services.AddGrpcClient<ApplicationService.ApplicationServiceClient>(options =>
        {
            var address = builder.Configuration["Application:ConnectionString"] ??
                throw new ArgumentException("gRPC Connection String cannot be empty!");
            options.Address = new Uri(address);
        });

        builder.Services.AddGrpcClient<ImageProcessorService.ImageProcessorServiceClient>(options =>
        {
            var address = builder.Configuration["ImageProcessor:ConnectionString"] ??
                throw new ArgumentException("ImageProcessor:ConnectionString must be set in configuration.");
            options.Address = new Uri(address);
        });

        var otlpEndpoint = new Uri(builder.Configuration["Otlp:Endpoint"] ?? "http://localhost:4317");

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName: "MehrakDashboard", serviceInstanceId: Environment.MachineName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddGrpcClientInstrumentation()
                .AddSource("MehrakDashboard")
                .AddOtlpExporter(o => o.Endpoint = otlpEndpoint))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter("MehrakDashboard")
                .AddOtlpExporter((o, m) =>
                {
                    o.Endpoint = otlpEndpoint;
                    m.TemporalityPreference = MetricReaderTemporalityPreference.Delta;
                }));

        builder.Services.AddDashboardApplicationExecutor();

        var cookieDomain = new Uri(builder.Configuration["Dashboard:Origin"]
            ?? throw new ArgumentException("Dashboard:Origin cannot be empty.")).Host;

        builder.Services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "mehrak.dashboard.auth";
                options.Cookie.Domain = cookieDomain;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.SlidingExpiration = false;
                options.ExpireTimeSpan = TimeSpan.FromHours(1);
                options.EventsType = typeof(DashboardCookieEvents);
            });

        builder.Services.AddOpenIddict()
            .AddCore(options =>
            {
                options.UseEntityFrameworkCore()
                       .UseDbContext<DashboardAuthDbContext>();
            })
            .AddClient(options =>
            {
                options.AllowAuthorizationCodeFlow();

                options.AddDevelopmentEncryptionCertificate()
                       .AddDevelopmentSigningCertificate();

                var aspnetcoreBuilder = options.UseAspNetCore()
                       .EnableRedirectionEndpointPassthrough()
                       .EnableStatusCodePagesIntegration();

                aspnetcoreBuilder.Configure(options =>
                {
                    options.CookieBuilder.Domain = cookieDomain;
                });

                if (builder.Environment.IsDevelopment())
                    aspnetcoreBuilder.DisableTransportSecurityRequirement();

                options.UseSystemNetHttp();

                options.UseWebProviders()
                       .AddDiscord(options =>
                       {
                           options.SetClientId(builder.Configuration["Discord:ClientId"] ?? throw new ArgumentException("Discord:ClientId cannot be empty."))
                                  .SetClientSecret(builder.Configuration["Discord:ClientSecret"] ?? throw new ArgumentException("Discord:ClientSecret cannot be empty."))
                                  .SetRedirectUri("/auth/callback");
                       });
            });

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy("RequireSuperAdmin", policy =>
                policy.RequireRole("superadmin"))
            .AddPolicy("RequireGameWrite", policy =>
                policy.RequireAssertion(ctx =>
                    ctx.User.IsInRole("superadmin") ||
                    ctx.User.HasClaim(c =>
                        c.Type == "perm" &&
                        c.Value.StartsWith("game_write:", StringComparison.OrdinalIgnoreCase))));

        if (builder.Environment.IsProduction())
        {
            builder.Services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.KnownProxies.Add(IPAddress.Parse(builder.Configuration["Nginx:KnownProxy"] ?? "127.0.0.1"));
            });
        }

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy("login", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));
        });

        builder.Services.AddControllers();

        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(b =>
            {
                b.WithOrigins(builder.Configuration["Dashboard:Origin"] ?? throw new ArgumentException("Dashboard Origin cannot be empty"))
                    .AllowAnyHeader()
                    .AllowCredentials()
                    .AllowAnyMethod();
            });
        });

        var app = builder.Build();
        await SeedRootUserIfNeeded(app);
        await ReleaseNoteSeedData.SeedReleaseNotesAsync(app);

        app.UseForwardedHeaders();
        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.MapControllers();
        app.MapReverseProxy();

        await app.RunAsync();
    }

    private static async Task SeedRootUserIfNeeded(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DashboardAuthDbContext>();

        if (await db.DashboardUsers.AnyAsync(u => u.IsRootUser))
            return;

        var adminDiscordId = app.Configuration["Dashboard:AdminDiscordId"];

        if (string.IsNullOrWhiteSpace(adminDiscordId) ||
            !long.TryParse(adminDiscordId, out var discordId))
        {
            throw new ArgumentException("Dashboard:AdminDiscordId must be set in configuration.");
        }

        if (await db.DashboardUsers.AnyAsync(u => u.DiscordId == discordId))
            return;

        var user = new DashboardUser
        {
            Username = discordId.ToString(),
            DiscordId = discordId,
            IsSuperAdmin = true,
            IsActive = true,
            IsRootUser = true
        };

        db.DashboardUsers.Add(user);
        await db.SaveChangesAsync();
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

    private static string NormalizeAbsoluteUrl(string configuredValue, string key)
    {
        var trimmed = configuredValue.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed))
            throw new ArgumentException($"{key} must be a valid absolute URL.");

        var normalized = parsed.AbsoluteUri;
        return normalized.EndsWith('/') ? normalized : normalized + "/";
    }
}
