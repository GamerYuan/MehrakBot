using Mehrak.Domain.Enums;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.Infrastructure.Metrics;
using Mehrak.Infrastructure.Repositories;
using Mehrak.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace Mehrak.Infrastructure;

public static class InfrastructureServiceCollectionExtension
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddSingleton<MongoDbService>();
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

        services.AddSingleton<CookieEncryptionService>();

        return services;
    }
}
