#region

using Mehrak.Application.Models.Context;
using Mehrak.Application.Services.Common;
using Mehrak.Application.Services.Genshin;
using Mehrak.Application.Services.Hi3;
using Mehrak.Application.Services.Hsr;
using Mehrak.Application.Services.Zzz;
using Mehrak.Bot.Services;
using Mehrak.Domain.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;

#endregion

namespace Mehrak.Application;

public static class ApplicationServiceCollectionExtension
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddTransient<IApplicationService<CheckInApplicationContext>, DailyCheckInService>();
        services.AddTransient<IApplicationService<CodeRedeemApplicationContext>, CodeRedeemApplicationService>();

        services.AddHostedService<AsyncInitializationHostedService>();

        services.AddGenshinApplicationServices();
        services.AddHsrApplicationServices();
        services.AddZzzApplicationServices();
        services.AddHi3ApplicationServices();

        return services;
    }
}
