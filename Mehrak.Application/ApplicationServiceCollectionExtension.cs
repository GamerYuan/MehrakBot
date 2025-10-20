using Mehrak.Application.Models.Context;
using Mehrak.Application.Services.Common;
using Mehrak.Application.Services.Genshin;
using Mehrak.Application.Services.Hsr;
using Mehrak.Domain.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Mehrak.Application;

public static class ApplicationServiceCollectionExtension
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddTransient<IApplicationService<CodeRedeemApplicationContext>, CodeRedeemApplicationService>();

        services.AddGenshinApplicationServices();
        services.AddHsrApplicationServices();

        return services;
    }
}
