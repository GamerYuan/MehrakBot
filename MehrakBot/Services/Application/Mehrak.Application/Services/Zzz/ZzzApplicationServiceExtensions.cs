#region

using Mehrak.Application.Services.Abstractions;
using Mehrak.Application.Services.Zzz.Assault;
using Mehrak.Application.Services.Zzz.Character;
using Mehrak.Application.Services.Zzz.Defense;
using Mehrak.Application.Services.Zzz.RealTimeNotes;
using Mehrak.Domain;
using Mehrak.Domain.Common;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Zzz.Types;

#endregion

namespace Mehrak.Application.Services.Zzz;

internal static class ZzzApplicationServiceExtensions
{
    internal static IServiceCollection AddZzzApplicationServices(this IServiceCollection services)
    {
        services.AddKeyedTransient<IApplicationService, ZzzAssaultApplicationService>(CommandName.Zzz.Assault);
        services.AddSingleton<ICardService<ZzzAssaultData>, ZzzAssaultCardService>();
        services.RegisterAsyncInitializableFor<ICardService<ZzzAssaultData>, ZzzAssaultCardService>();

        services.AddKeyedTransient<IApplicationService, ZzzCharacterApplicationService>(CommandName.Zzz.Character);
        services.AddSingleton<ICardService<ZzzFullAvatarData>, ZzzCharacterCardService>();
        services.RegisterAsyncInitializableFor<ICardService<ZzzFullAvatarData>, ZzzCharacterCardService>();

        services.AddKeyedTransient<IApplicationService, ZzzDefenseApplicationService>(CommandName.Zzz.Defense);
        services.AddSingleton<ICardService<ZzzDefenseData>, ZzzDefenseCardService>();
        services.RegisterAsyncInitializableFor<ICardService<ZzzDefenseData>, ZzzDefenseCardService>();

        services.AddKeyedTransient<IApplicationService, ZzzRealTimeNotesApplicationService>(CommandName.Zzz.RealTimeNotes);

        return services;
    }
}
