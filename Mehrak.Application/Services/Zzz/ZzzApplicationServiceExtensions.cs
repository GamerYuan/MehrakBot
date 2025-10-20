using Mehrak.Application.Services.Zzz.Assault;
using Mehrak.Application.Services.Zzz.Character;
using Mehrak.Application.Services.Zzz.Defense;
using Mehrak.Application.Services.Zzz.RealTimeNotes;
using Mehrak.Domain;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Zzz.Types;
using Microsoft.Extensions.DependencyInjection;

namespace Mehrak.Application.Services.Zzz;

internal static class ZzzApplicationServiceExtensions
{
    internal static IServiceCollection AddZzzApplicationServices(this IServiceCollection services)
    {
        services.AddTransient<IApplicationService<ZzzAssaultApplicationContext>, ZzzAssaultApplicationService>();
        services.AddSingleton<ICardService<ZzzAssaultData>, ZzzAssaultCardService>();
        services.RegisterAsyncInitializableFor<ICardService<ZzzAssaultData>, ZzzAssaultCardService>();

        services.AddTransient<IApplicationService<ZzzCharacterApplicationContext>, ZzzCharacterApplicationService>();
        services.AddSingleton<ICardService<ZzzFullAvatarData>, ZzzCharacterCardService>();
        services.RegisterAsyncInitializableFor<ICardService<ZzzFullAvatarData>, ZzzCharacterCardService>();

        services.AddTransient<IApplicationService<ZzzDefenseApplicationContext>, ZzzDefenseApplicationService>();
        services.AddSingleton<ICardService<ZzzDefenseData>, ZzzDefenseCardService>();
        services.RegisterAsyncInitializableFor<ICardService<ZzzDefenseData>, ZzzDefenseCardService>();

        services.AddTransient<IApplicationService<ZzzRealTimeNotesApplicationContext>, ZzzRealTimeNotesApplicationService>();

        return services;
    }
}
