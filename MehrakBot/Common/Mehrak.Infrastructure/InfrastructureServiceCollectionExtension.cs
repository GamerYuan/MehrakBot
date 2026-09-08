#region

using Amazon.S3;
using Mehrak.Domain.Auth;
using Mehrak.Domain.Cache;
using Mehrak.Domain.Character;
using Mehrak.Domain.Image;
using Mehrak.Domain.Shared.Services;
using Mehrak.Infrastructure.Auth;
using Mehrak.Infrastructure.Auth.Services;
using Mehrak.Infrastructure.Character;
using Mehrak.Infrastructure.Character.Services;
using Mehrak.Infrastructure.CodeRedeem;
using Mehrak.Infrastructure.Documentation;
using Mehrak.Infrastructure.ReleaseNote;
using Mehrak.Infrastructure.Relic;
using Mehrak.Infrastructure.Shared;
using Mehrak.Infrastructure.Shared.Cache;
using Mehrak.Infrastructure.Shared.Config;
using Mehrak.Infrastructure.Shared.Storage;
using Mehrak.Infrastructure.User;
using Mehrak.Infrastructure.User.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

#endregion

namespace Mehrak.Infrastructure;

public static class InfrastructureServiceCollectionExtension
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddDbContext<DashboardAuthDbContext>((sp, options) =>
        {
            options.UseNpgsql(GetRuntimeConnectionString(sp));
            options.UseOpenIddict();
        });
        services.AddDbContext<CharacterDbContext>((sp, options) =>
            options.UseNpgsql(GetRuntimeConnectionString(sp)));
        services.AddDbContext<UserDbContext>((sp, options) =>
            options.UseNpgsql(GetRuntimeConnectionString(sp)));
        services.AddDbContext<CodeRedeemDbContext>((sp, options) =>
            options.UseNpgsql(GetRuntimeConnectionString(sp)));
        services.AddDbContext<RelicDbContext>((sp, options) =>
            options.UseNpgsql(GetRuntimeConnectionString(sp)));
        services.AddDbContext<DocumentationDbContext>((sp, options) =>
            options.UseNpgsql(GetRuntimeConnectionString(sp)));
        services.AddDbContext<ReleaseNoteDbContext>((sp, options) =>
            options.UseNpgsql(GetRuntimeConnectionString(sp)));

        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var redisConfig = sp.GetRequiredService<IOptions<RedisConfig>>().Value;
            return ConnectionMultiplexer.Connect(redisConfig.ConnectionString);
        });
        services.AddStackExchangeRedisCache(_ => { });
        services.AddOptions<RedisCacheOptions>()
            .Configure<IConnectionMultiplexer, IOptions<RedisConfig>>((options, connection, redisConfig) =>
            {
                options.ConnectionMultiplexerFactory = () => Task.FromResult(connection);
                options.InstanceName = redisConfig.Value.InstanceName;
            });

        services.AddTransient<IDbStatusService, DbStatusService>();

        services.AddSingleton<IAmazonS3>(sp =>
        {
            var cfg = sp.GetRequiredService<IOptions<S3StorageConfig>>().Value;
            var s3Config = new AmazonS3Config
            {
                ServiceURL = cfg.ServiceURL,
                ForcePathStyle = cfg.ForcePathStyle,
                Timeout = TimeSpan.FromSeconds(30),
                SignatureMethod = Amazon.Runtime.SigningAlgorithm.HmacSHA256
            };
            return new AmazonS3Client(cfg.AccessKey, cfg.SecretKey, s3Config);
        });

        services.AddSingleton<IImageRepository, ImageRepository>();

        services.AddSingleton<IAttachmentStorageService, AttachmentStorageService>();
        services.AddSingleton<ICacheService, RedisCacheService>();

        // Character Cache Services
        services.AddSingleton<ICharacterCacheService, CharacterCacheService>();
        services.AddSingleton<IAliasService, AliasService>();

        services.AddSingleton<ICharacterStatService, CharacterStatService>();
        services.AddSingleton<ICharacterPortraitConfigService, CharacterPortraitConfigService>();

        services.AddScoped<IDashboardSessionService, DashboardSessionService>();

        services.AddSingleton<UserCountTrackerService>();

        services.AddSingleton<IUserPortraitService, UserPortraitService>();
        services.AddSingleton<IPortraitUploadRateLimitService, PortraitUploadRateLimitService>();
        services.AddSingleton<IPassphraseAttemptRateLimiter, PassphraseAttemptRateLimiter>();

        services.AddSingleton<IEncryptionService, CookieEncryptionService>();

        services.AddMemoryCache();

        return services;
    }

    // Runtime services support a dedicated least-privilege PostgreSQL role via
    // ConnectionStrings:mehrakdb_runtime (see POSTGRES_RUNTIME_USER /
    // POSTGRES_RUNTIME_PASSWORD in .env.template). When it is unset, fall back
    // to the shared mehrakdb connection string so existing deployments keep
    // working unchanged.
    private static string GetRuntimeConnectionString(IServiceProvider serviceProvider)
    {
        var runtimeConnectionString = serviceProvider
            .GetRequiredService<IConfiguration>()
            .GetConnectionString("mehrakdb_runtime");
        if (!string.IsNullOrWhiteSpace(runtimeConnectionString))
        {
            return runtimeConnectionString;
        }

        return serviceProvider.GetRequiredService<IOptions<PgConfig>>().Value.ConnectionString;
    }
}
