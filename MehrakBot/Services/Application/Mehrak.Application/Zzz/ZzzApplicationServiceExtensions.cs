#region

using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Zzz.Assault;
using Mehrak.Application.Zzz.Character;
using Mehrak.Application.Zzz.CharList;
using Mehrak.Application.Zzz.Defense;
using Mehrak.Application.Zzz.RealTimeNotes;
using Mehrak.Application.Zzz.Tower;
using Mehrak.Domain;
using Mehrak.Domain.Card;
using Mehrak.Domain.Shared.Common;
using Mehrak.GameApi.Zzz.Types;

#endregion

namespace Mehrak.Application.Zzz;

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
        services.AddSingleton<ICardService<ZzzDefenseDataV2>, ZzzDefenseCardService>();
        services.RegisterAsyncInitializableFor<ICardService<ZzzDefenseDataV2>, ZzzDefenseCardService>();

        services.AddKeyedTransient<IApplicationService, ZzzRealTimeNotesApplicationService>(CommandName.Zzz.RealTimeNotes);

        services.AddKeyedTransient<IApplicationService, ZzzCharListApplicationService>(CommandName.Zzz.CharList);
        services.AddSingleton<ICardService<(IEnumerable<ZzzBasicAvatarData>, IEnumerable<ZzzBuddyData>)>, ZzzCharListCardService>();
        services.RegisterAsyncInitializableFor<ICardService<(IEnumerable<ZzzBasicAvatarData>, IEnumerable<ZzzBuddyData>)>, ZzzCharListCardService>();

        services.AddKeyedTransient<IApplicationService, ZzzTowerApplicationService>(CommandName.Zzz.Tower);
        services.AddSingleton<ICardService<ZzzTowerData>, ZzzTowerCardService>();
        services.RegisterAsyncInitializableFor<ICardService<ZzzTowerData>, ZzzTowerCardService>();

        return services;
    }
}
