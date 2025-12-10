#region

using Amazon.S3;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.Infrastructure.Config;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Metrics;
using Mehrak.Infrastructure.Migrations;
using Mehrak.Infrastructure.Repositories;
using Mehrak.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

#endregion

namespace Mehrak.Infrastructure;

public static class InfrastructureServiceCollectionExtension
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<UserDbContext>(options => options.UseNpgsql(config["Postgres:ConnectionString"]));
        services.AddDbContext<CharacterDbContext>(options => options.UseNpgsql(config["Postgres:ConnectionString"]));
        services.AddDbContext<RelicDbContext>(options => options.UseNpgsql(config["Postgres:ConnectionString"]));
        services.AddDbContext<CodeRedeemDbContext>(options => options.UseNpgsql(config["Postgres:ConnectionString"]));

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

        services.AddSingleton<MongoDbService>();
        services.AddHostedService<MongoToSqlMigrator>();

        services.AddSingleton<IUserRepository, UserRepository>();
        services.AddSingleton<IImageRepository, ImageRepository>();
        services.AddSingleton<ICharacterRepository, CharacterRepository>();
        services.AddSingleton<IAliasRepository, AliasRepository>();
        services.AddSingleton<ICodeRedeemRepository, CodeRedeemRepository>();
        services.AddSingleton<IRelicRepository, HsrRelicRepository>();
        BsonSerializer.RegisterSerializer(new EnumSerializer<Game>(BsonType.String));

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
