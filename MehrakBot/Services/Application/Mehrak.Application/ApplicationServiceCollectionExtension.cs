#region

using Mehrak.Application.CodeRedeem;
using Mehrak.Application.DailyCheckIn;
using Mehrak.Application.Genshin;
using Mehrak.Application.Hsr;
using Mehrak.Application.Services.Hi3;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Services;
using Mehrak.Application.Zzz;
using Mehrak.Domain.Image.Abstractions;
using Mehrak.Domain.Shared.Common;

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

        services.AddGenshinApplicationServices();
        services.AddHsrApplicationServices();
        services.AddZzzApplicationServices();
        services.AddHi3ApplicationServices();

        services.AddKeyedSingleton<IMultiImageProcessor, WeaponImageProcessorGrpcClient>(CommandName.ImageProcessor.Weapon);

        return services;
    }
}
