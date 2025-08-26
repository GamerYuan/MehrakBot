using MehrakCore.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MehrakCore.Utility;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection RegisterAsyncInitializable<T>(
        this IServiceCollection services)
        where T : class, IAsyncInitializable
    {
        services.AddScoped<IAsyncInitializable>(sp => sp.GetRequiredService<T>());
        return services;
    }
}
