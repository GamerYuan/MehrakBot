#region

using Mehrak.Domain.Shared.Storage;
using Mehrak.Infrastructure.Shared.Storage;
using Microsoft.Extensions.DependencyInjection;

#endregion

namespace Mehrak.Infrastructure;

public static class AssetCleanupServiceCollectionExtension
{
    public static IServiceCollection AddAssetCleanup(this IServiceCollection services)
    {
        services.AddSingleton<IAssetSuffixCleanupService, AssetSuffixCleanupService>();
        return services;
    }
}
