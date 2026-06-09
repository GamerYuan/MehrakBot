#region

using Mehrak.Application.Hsr.Anomaly;
using Mehrak.Application.Hsr.Character;
using Mehrak.Application.Hsr.CharList;
using Mehrak.Application.Hsr.EndGame;
using Mehrak.Application.Hsr.Memory;
using Mehrak.Application.Hsr.RealTimeNotes;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Domain;
using Mehrak.Domain.Card;
using Mehrak.Domain.Shared.Common;
using Mehrak.Domain.Shared.Enums;
using Mehrak.GameApi.Hsr.Types;

#endregion

namespace Mehrak.Application.Hsr;

internal static class HsrApplicationServiceExtensions
{
    internal static IServiceCollection AddHsrApplicationServices(this IServiceCollection services)
    {
        services.AddKeyedTransient<IApplicationService, HsrCharacterApplicationService>(CommandName.Hsr.Character);
        services.AddSingleton<ICardService<HsrCharacterInformation>, HsrCharacterCardService>();
        services.RegisterAsyncInitializableFor<ICardService<HsrCharacterInformation>, HsrCharacterCardService>();

        services.AddKeyedTransient<IApplicationService, HsrCharListApplicationService>(CommandName.Hsr.CharList);
        services.AddSingleton<ICardService<IEnumerable<HsrCharacterInformation>>, HsrCharListCardService>();
        services.RegisterAsyncInitializableFor<ICardService<IEnumerable<HsrCharacterInformation>>, HsrCharListCardService>();

        services.AddKeyedTransient<IApplicationService, HsrEndGameApplicationService>(CommandName.Hsr.PureFiction);
        services.AddKeyedTransient<IApplicationService, HsrEndGameApplicationService>(CommandName.Hsr.ApocalypticShadow);

        services.AddKeyedSingleton<ICardService<HsrEndInformation>, HsrPureFictionCardService>(HsrEndGameMode.PureFiction);
        services.RegisterAsyncInitializableForKeyed<ICardService<HsrEndInformation>, HsrPureFictionCardService>(HsrEndGameMode.PureFiction);

        services.AddKeyedSingleton<ICardService<HsrEndInformation>, HsrApocalypticShadowCardService>(HsrEndGameMode.ApocalypticShadow);
        services.RegisterAsyncInitializableForKeyed<ICardService<HsrEndInformation>, HsrApocalypticShadowCardService>(HsrEndGameMode.ApocalypticShadow);

        services.AddKeyedTransient<IApplicationService, HsrMemoryApplicationService>(CommandName.Hsr.Memory);
        services.AddSingleton<ICardService<HsrMemoryInformation>, HsrMemoryCardService>();
        services.RegisterAsyncInitializableFor<ICardService<HsrMemoryInformation>, HsrMemoryCardService>();

        services.AddKeyedTransient<IApplicationService, HsrAnomalyApplicationService>(CommandName.Hsr.Anomaly);
        services.AddSingleton<ICardService<HsrAnomalyInformation>, HsrAnomalyCardService>();
        services.RegisterAsyncInitializableFor<ICardService<HsrAnomalyInformation>, HsrAnomalyCardService>();

        services.AddKeyedTransient<IApplicationService, HsrRealTimeNotesApplicationService>(CommandName.Hsr.RealTimeNotes);

        return services;
    }
}
