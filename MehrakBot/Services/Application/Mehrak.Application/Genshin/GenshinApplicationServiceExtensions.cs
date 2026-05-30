#region

using Mehrak.Application.Genshin.Abyss;
using Mehrak.Application.Genshin.Character;
using Mehrak.Application.Genshin.CharList;
using Mehrak.Application.Genshin.RealTimeNotes;
using Mehrak.Application.Genshin.Stygian;
using Mehrak.Application.Services.Genshin.Theater;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Domain;
using Mehrak.Domain.Card;
using Mehrak.Domain.Shared.Common;
using Mehrak.GameApi.Genshin.Types;

#endregion

namespace Mehrak.Application.Genshin;

internal static class GenshinApplicationServiceExtensions
{
    internal static IServiceCollection AddGenshinApplicationServices(this IServiceCollection services)
    {
        services
            .AddSingleton<ICardService<GenshinAbyssInformation>,
                GenshinAbyssCardService>();
        services.RegisterAsyncInitializableFor<
            ICardService<GenshinAbyssInformation>,
            GenshinAbyssCardService>();
        services.AddKeyedTransient<IApplicationService, GenshinAbyssApplicationService>(CommandName.Genshin.Abyss);

        services.AddSingleton<ICardService<GenshinCharacterInformation>, GenshinCharacterCardService>();
        services
            .RegisterAsyncInitializableFor<ICardService<GenshinCharacterInformation>, GenshinCharacterCardService>();
        services.AddKeyedTransient<IApplicationService, GenshinCharacterApplicationService>(CommandName.Genshin.Character);

        services.AddSingleton<ICardService<IEnumerable<GenshinBasicCharacterData>>, GenshinCharListCardService>();
        services.RegisterAsyncInitializableFor<ICardService<IEnumerable<GenshinBasicCharacterData>>, GenshinCharListCardService>();
        services.AddKeyedTransient<IApplicationService, GenshinCharListApplicationService>(CommandName.Genshin.CharList);

        services.AddSingleton<ICardService<StygianData>, GenshinStygianCardService>();
        services.RegisterAsyncInitializableFor<ICardService<StygianData>, GenshinStygianCardService>();
        services.AddKeyedTransient<IApplicationService, GenshinStygianApplicationService>(CommandName.Genshin.Stygian);

        services.AddSingleton<ICardService<GenshinTheaterInformation>,
                GenshinTheaterCardService>();
        services.RegisterAsyncInitializableFor<ICardService<GenshinTheaterInformation>, GenshinTheaterCardService>();
        services.AddKeyedTransient<IApplicationService, GenshinTheaterApplicationService>(CommandName.Genshin.Theater);

        services.AddKeyedTransient<IApplicationService, GenshinRealTimeNotesApplicationService>(CommandName.Genshin.RealTimeNotes);

        return services;
    }
}
