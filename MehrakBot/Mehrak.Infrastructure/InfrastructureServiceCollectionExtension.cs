#region

using Amazon.S3;
using Mehrak.Domain.Services;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.Infrastructure.Config;
using Mehrak.Infrastructure.Metrics;
using Mehrak.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

#endregion

namespace Mehrak.Infrastructure;

public static class InfrastructureServiceCollectionExtension
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration config)
    {
        IConnectionMultiplexer multiplexer = ConnectionMultiplexer.Connect(
            config["Redis:ConnectionString"] ?? "localhost:6379");
        services.AddSingleton(multiplexer);
        services.AddStackExchangeRedisCache(options =>
        {
            options.ConnectionMultiplexerFactory = () => Task.FromResult(multiplexer);
            options.InstanceName = "MehrakBot_";
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

        services.AddSingleton<IAttachmentStorageService, AttachmentStorageService>();

        services.AddSingleton<ICacheService, RedisCacheService>();

        // Character Cache Services
        services.AddHostedService<CharacterInitializationService>();
        services.AddHostedService<AliasInitializationService>();
        services.AddHostedService<CharacterCacheBackgroundService>();
        services.AddSingleton<ICharacterCacheService, CharacterCacheService>();

        services.AddSingleton<IMetricsService, BotMetricsService>();
        services.AddHostedService<BotMetricsService>();

        services.AddSingleton<ISystemResourceClientService, PrometheusClientService>();

        services.AddSingleton<IEncryptionService, CookieEncryptionService>();

        return services;
    }
}
