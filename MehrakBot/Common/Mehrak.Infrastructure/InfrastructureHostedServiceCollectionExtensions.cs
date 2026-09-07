using Mehrak.Infrastructure.Auth.Services;
using Mehrak.Infrastructure.Character.Services;
using Mehrak.Infrastructure.Shared.Storage;
using Mehrak.Infrastructure.User.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Mehrak.Infrastructure;

public static class InfrastructureHostedServiceCollectionExtensions
{
    /// <summary>
    /// Registers the one-time infrastructure initialization owned by the Application process.
    /// Application refreshes the shared character and alias caches and configures attachment storage;
    /// Bot and Dashboard consume those shared resources without repeating the work.
    /// </summary>
    public static IServiceCollection AddApplicationInfrastructureHostedServices(this IServiceCollection services)
    {
        services.AddHostedService<AttachmentStorageInitializer>();
        services.AddHostedService<CharacterInitializationService>();
        services.AddHostedService<AliasInitializationService>();
        return services;
    }

    /// <summary>
    /// Registers the user-count backfill owned by the Bot process, which publishes the count used by its presence.
    /// Profile mutations from Bot and Dashboard continue to update the same shared tracker.
    /// </summary>
    public static IServiceCollection AddBotInfrastructureHostedServices(this IServiceCollection services)
    {
        services.AddHostedService<UserTrackerBackfillService>();
        return services;
    }

    /// <summary>
    /// Registers dashboard-only maintenance for expired authentication sessions.
    /// </summary>
    public static IServiceCollection AddDashboardInfrastructureHostedServices(this IServiceCollection services)
    {
        services.AddHostedService<SessionCleanupService>();
        return services;
    }
}
