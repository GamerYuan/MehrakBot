#region

using Mehrak.Application.Services.Abstractions;
using Mehrak.Application.Services.Common;
using Mehrak.Application.Services.Genshin;
using Mehrak.Application.Services.Hi3;
using Mehrak.Application.Services.Hsr;
using Mehrak.Application.Services.Zzz;
using Mehrak.Domain.Common;
using Mehrak.Infrastructure.Services;

#endregion

namespace Mehrak.Application;

internal static class ApplicationServiceCollectionExtension
{
    internal static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddKeyedTransient<IApplicationService, DailyCheckInService>(CommandName.Common.CheckIn);
        services.AddKeyedTransient<IApplicationService, CodeRedeemApplicationService>(CommandName.Genshin.Codes);
        services.AddKeyedTransient<IApplicationService, CodeRedeemApplicationService>(CommandName.Hsr.Codes);
        services.AddKeyedTransient<IApplicationService, CodeRedeemApplicationService>(CommandName.Zzz.Codes);

        services.AddHostedService<AsyncInitializationHostedService>();
        services.AddHostedService<CharacterCacheBackgroundService>();

        services.AddGenshinApplicationServices();
        services.AddHsrApplicationServices();
        services.AddZzzApplicationServices();
        services.AddHi3ApplicationServices();

        return services;
    }
}
