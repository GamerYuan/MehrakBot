#region

using Mehrak.Application.Services.Abstractions;
using Mehrak.Application.Services.Hsr.Anomaly;
using Mehrak.Application.Services.Hsr.Character;
using Mehrak.Application.Services.Hsr.CharList;
using Mehrak.Application.Services.Hsr.EndGame;
using Mehrak.Application.Services.Hsr.Memory;
using Mehrak.Application.Services.Hsr.RealTimeNotes;
using Mehrak.Domain;
using Mehrak.Domain.Common;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Hsr.Types;

#endregion

namespace Mehrak.Application.Services.Hsr;

internal static class HsrApplicationServiceExtensions
{
    internal static IServiceCollection AddHsrApplicationServices(this IServiceCollection services)
    {
        services.AddKeyedTransient<IApplicationService, HsrCharacterApplicationService>(CommandName.Hsr.Character);
        services.AddSingleton<ICardService<HsrCharacterInformation>, HsrCharacterCardService>();
        services.RegisterAsyncInitializableFor<ICardService<HsrCharacterInformation>, HsrCharacterCardService>();

        services.AddKeyedTransient<IApplicationService, HsrCharListApplicationService>(CommandName.Hsr.CharList);
        services.AddSingleton<ICardService<IEnumerable<HsrCharacterInformation>>, HsrCharListCardService>();

        services.AddKeyedTransient<IApplicationService, HsrEndGameApplicationService>(CommandName.Hsr.PureFiction);
        services.AddKeyedTransient<IApplicationService, HsrEndGameApplicationService>(CommandName.Hsr.ApocalypticShadow);
        services.AddSingleton<ICardService<HsrEndInformation>, HsrEndGameCardService>();
        services.RegisterAsyncInitializableFor<ICardService<HsrEndInformation>,
            HsrEndGameCardService>();

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
