#region

using Amazon.S3;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.Infrastructure.Auth;
using Mehrak.Infrastructure.Config;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Repositories;
using Mehrak.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
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
            options.UseNpgsql(sp.GetRequiredService<IOptions<PgConfig>>().Value.ConnectionString));
        services.AddDbContext<CharacterDbContext>((sp, options) =>
            options.UseNpgsql(sp.GetRequiredService<IOptions<PgConfig>>().Value.ConnectionString));
        services.AddDbContext<UserDbContext>((sp, options) =>
            options.UseNpgsql(sp.GetRequiredService<IOptions<PgConfig>>().Value.ConnectionString));
        services.AddDbContext<CodeRedeemDbContext>((sp, options) =>
            options.UseNpgsql(sp.GetRequiredService<IOptions<PgConfig>>().Value.ConnectionString));
        services.AddDbContext<RelicDbContext>((sp, options) =>
            options.UseNpgsql(sp.GetRequiredService<IOptions<PgConfig>>().Value.ConnectionString));

        using var serviceProvider = services.BuildServiceProvider();

        var redisConfig = serviceProvider.GetRequiredService<IOptions<RedisConfig>>().Value;
        var lazyConnection = new Lazy<IConnectionMultiplexer>(() =>
        {
            return ConnectionMultiplexer.Connect(redisConfig.ConnectionString);
        });
        services.AddSingleton(sp => lazyConnection.Value);
        services.AddStackExchangeRedisCache(options =>
        {
            options.ConnectionMultiplexerFactory = () => Task.FromResult(lazyConnection.Value);
            options.InstanceName = redisConfig.InstanceName;
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
        services.AddHostedService<CharacterInitializationService>();
        services.AddHostedService<AliasInitializationService>();
        services.AddSingleton<ICharacterCacheService, CharacterCacheService>();
        services.AddSingleton<IAliasService, AliasService>();

        services.AddSingleton<ICharacterStatService, CharacterStatService>();

        services.AddSingleton<IEncryptionService, CookieEncryptionService>();

        return services;
    }
}
