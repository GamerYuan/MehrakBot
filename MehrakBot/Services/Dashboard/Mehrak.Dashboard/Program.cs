using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Mehrak.Dashboard.Auth;
using Mehrak.Dashboard.Services;
using Mehrak.Domain.Auth;
using Mehrak.Domain.Protobuf;
using Mehrak.Infrastructure;
using Mehrak.Infrastructure.Auth;
using Mehrak.Infrastructure.Auth.Entities;
using Mehrak.Infrastructure.Auth.Services;
using Mehrak.Infrastructure.Config;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.OpenTelemetry;

namespace Mehrak.Dashboard;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Configuration.AddJsonFile("appsettings.json")
            .AddUserSecrets<Program>()
            .AddEnvironmentVariables();

        if (builder.Environment.IsDevelopment())
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

        // Configure logging to use Serilog
        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(dispose: true);

        builder.Services.Configure<S3StorageConfig>(builder.Configuration.GetSection("Storage"));
        builder.Services.Configure<CharacterCacheConfig>(builder.Configuration.GetSection("CharacterCache"));

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

        builder.Services.AddGrpcClient<ApplicationService.ApplicationServiceClient>(options =>
        {
            var address = builder.Configuration["Application:ConnectionString"] ??
                throw new ArgumentException("gRPC Connection String cannot be empty!");
            options.Address = new Uri(address);
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName: "MehrakDashboard", serviceInstanceId: Environment.MachineName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddGrpcClientInstrumentation()
                .AddSource("MehrakDashboard")
                .AddOtlpExporter())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter("MehrakDashboard")
                .AddOtlpExporter());

        builder.Services.AddDashboardApplicationExecutor();

        builder.Services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "mehrak.dashboard.auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.SlidingExpiration = false;
                options.ExpireTimeSpan = TimeSpan.FromHours(1);
                options.EventsType = typeof(DashboardCookieEvents);
                options.LoginPath = "/auth/login";
            });

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy("RequireSuperAdmin", policy =>
                policy.RequireRole("superadmin"))
            .AddPolicy("RequireGameWrite", policy =>
                policy.RequireAssertion(ctx =>
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
            if (builder.Environment.IsDevelopment())
            {
                options.AddDefaultPolicy(b =>
                {
                    b.WithOrigins("http://localhost:5173")
                        .AllowAnyHeader()
                        .AllowCredentials()
                        .AllowAnyMethod();
                });
            }
            else
            {
                options.AddDefaultPolicy(b =>
                {
                    b.WithOrigins(builder.Configuration["Dashboard:Origin"] ?? throw new ArgumentException("Dashboard Origin cannot be empty"))
                        .AllowAnyHeader()
                        .AllowCredentials()
                        .AllowAnyMethod();
                });
            }
        });

        var app = builder.Build();
        await AddDefaultSuperAdminAccount(app);

        app.UseForwardedHeaders();
        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.MapControllers();

        await app.RunAsync();
    }

    private static async Task AddDefaultSuperAdminAccount(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DashboardAuthDbContext>();

        if (await db.DashboardUsers.AnyAsync(u => u.IsRootUser))
            return;

        var adminUsername = app.Configuration["Dashboard:AdminUsername"];
        var adminPassword = app.Configuration["Dashboard:AdminPassword"];
        var adminDiscordId = app.Configuration["Dashboard:AdminDiscordId"];

        if (string.IsNullOrWhiteSpace(adminUsername) ||
            string.IsNullOrWhiteSpace(adminPassword) ||
            string.IsNullOrWhiteSpace(adminDiscordId) ||
            !long.TryParse(adminDiscordId, out _))
        {
            throw new ArgumentException("Admin credentials are not set or not valid in configuration.");
        }

        if (!await db.DashboardUsers.AnyAsync(u => u.Username == adminUsername))
        {
            var hasher = new PasswordHasher<DashboardUser>();
            var user = new DashboardUser
            {
                Username = adminUsername,
                DiscordId = long.Parse(adminDiscordId),
                IsSuperAdmin = true,
                IsActive = true,
                RequirePasswordReset = false,
                IsRootUser = true
            };
            user.PasswordHash = hasher.HashPassword(user, adminPassword);

            db.DashboardUsers.Add(user);
            await db.SaveChangesAsync();
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
