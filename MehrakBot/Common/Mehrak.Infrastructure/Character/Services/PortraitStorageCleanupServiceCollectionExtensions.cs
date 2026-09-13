using Microsoft.Extensions.DependencyInjection;

namespace Mehrak.Infrastructure.Character.Services;

public static class PortraitStorageCleanupServiceCollectionExtensions
{
    public static IServiceCollection AddPortraitStorageCleanup(this IServiceCollection services)
    {
        services.AddSingleton<UserPortraitDeletionProcessor>();
        services.AddHostedService<UserPortraitDeletionHostedService>();
        return services;
    }
}
