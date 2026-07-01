using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using Mehrak.Dashboard.ReleaseNote;
using Mehrak.Dashboard.Shared.Auth;
using Mehrak.Dashboard.Shared.Services;
using Mehrak.Domain.Auth;
using Mehrak.Domain.Protobuf;
using Mehrak.Domain.Shared.Services;
using Mehrak.GameApi.GameRole;
using Mehrak.Infrastructure;
using Mehrak.Infrastructure.Auth;
using Mehrak.Infrastructure.Auth.Entities;
using Mehrak.Infrastructure.Auth.Services;
using Mehrak.Infrastructure.Character.Services;
using Mehrak.Infrastructure.Shared.Config;
using Mehrak.ServiceDefaults;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Yarp.ReverseProxy.Configuration;

namespace Mehrak.Dashboard;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.AddServiceDefaults();

        if (builder.Environment.IsDevelopment())
        {
            Console.WriteLine("Development environment detected");
        }

        builder.AddSerilogOtlp("MehrakDashboard");

        builder.Services.Configure<S3StorageConfig>(builder.Configuration.GetSection("Storage"));
        builder.Services.Configure<UserPortraitStorageConfig>(builder.Configuration.GetSection("UserPortraitStorage"));

        builder.Services.Configure<RedisConfig>(options =>
        {
            options.ConnectionString = builder.Configuration.GetConnectionString("redis") ?? options.ConnectionString;
            options.InstanceName = builder.Configuration.GetValue<string>("Redis:InstanceName") ?? "Mehrak_";
        });
        builder.Services.Configure<PgConfig>(options =>
            options.ConnectionString = builder.Configuration.GetConnectionString("mehrakdb") ?? options.ConnectionString);

        // Auth services
        builder.Services.AddScoped<IDashboardAuthService, DashboardAuthService>();
        builder.Services.AddScoped<IDashboardUserService, DashboardUserService>();

        builder.Services.AddScoped<IDashboardProfileAuthenticationService, DashboardProfileAuthenticationService>();
        builder.Services.AddScoped<DashboardCookieEvents>();

        builder.Services.AddInfrastructureServices();
        builder.Services.AddSingleton<GameRoleApiService>();

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
            var address = builder.Configuration.GetConnectionString("application") ?? "http://application";
            options.Address = new Uri(address);
        });

        builder.Services.AddGrpcClient<ImageProcessorService.ImageProcessorServiceClient>(options =>
        {
            var address = builder.Configuration.GetConnectionString("image-processor") ?? "http://image-processor";
            options.Address = new Uri(address);
        });

        builder.Services.AddSingleton<IImageClassificationService, ImageClassificationGrpcClient>();

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
                options.ExpireTimeSpan = TimeSpan.FromDays(7);
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

                if (builder.Environment.IsDevelopment())
                {
                    options.AddDevelopmentEncryptionCertificate()
                        .AddDevelopmentSigningCertificate();
                }
                else
                {
                    var encryptionCertPath = builder.Configuration["Dashboard:EncryptionCertificatePath"]
                        ?? "server-encryption-certificate.pfx";
                    var signingCertPath = builder.Configuration["Dashboard:SigningCertificatePath"]
                        ?? "server-signing-certificate.pfx";

                    var encryptionCert = X509CertificateLoader.LoadPkcs12FromFile(encryptionCertPath,
                        string.Empty, keyStorageFlags: X509KeyStorageFlags.DefaultKeySet);
                    var signingCert = X509CertificateLoader.LoadPkcs12FromFile(signingCertPath,
                        string.Empty, keyStorageFlags: X509KeyStorageFlags.DefaultKeySet);

                    options
                        .AddEncryptionCertificate(encryptionCert)
                        .AddSigningCertificate(signingCert);
                }

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
                options.KnownProxies.Clear();
                options.KnownIPNetworks.Clear();
            });
        }

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 100,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0,
                        SegmentsPerWindow = 10
                    }));

            options.AddPolicy("login", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(15),
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
        app.MapDefaultEndpoints();

        await app.RunAsync();
    }

    private static async Task SeedRootUserIfNeeded(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DashboardAuthDbContext>();

        var adminDiscordId = app.Configuration["Dashboard:AdminDiscordId"];

        if (string.IsNullOrWhiteSpace(adminDiscordId) ||
            !long.TryParse(adminDiscordId, out var discordId))
        {
            throw new ArgumentException("Dashboard:AdminDiscordId must be set in configuration.");
        }

        var permissions = new[]
        {
            new DashboardPermission { DiscordId = discordId, Permission = "rootuser" },
            new DashboardPermission { DiscordId = discordId, Permission = "superadmin" }
        };

        foreach (var permission in permissions)
        {
            if (!await db.DashboardPermissions.AnyAsync(p => p.DiscordId == discordId && p.Permission == permission.Permission))
                db.DashboardPermissions.Add(permission);
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Another instance likely inserted the same permissions concurrently
        }
    }

    private static string NormalizeAbsoluteUrl(string configuredValue, string key)
    {
        var trimmed = configuredValue.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed))
            throw new ArgumentException($"{key} must be a valid absolute URL.");

        var normalized = parsed.AbsoluteUri;
        return normalized.EndsWith('/') ? normalized : normalized + "/";
    }
}
