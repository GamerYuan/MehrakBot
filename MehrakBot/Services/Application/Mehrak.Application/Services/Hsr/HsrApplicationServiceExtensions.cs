#region

using Mehrak.Application.Services.Hsr.Anomaly;
using Mehrak.Application.Services.Hsr.Character;
using Mehrak.Application.Services.Hsr.CharList;
using Mehrak.Application.Services.Hsr.EndGame;
using Mehrak.Application.Services.Hsr.Memory;
using Mehrak.Application.Services.Hsr.RealTimeNotes;
using Mehrak.Domain;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Hsr.Types;
using Microsoft.Extensions.DependencyInjection;

#endregion

namespace Mehrak.Application.Services.Hsr;

internal static class HsrApplicationServiceExtensions
{
    internal static IServiceCollection AddHsrApplicationServices(this IServiceCollection services)
    {
        services.AddTransient<IApplicationService<HsrCharacterApplicationContext>, HsrCharacterApplicationService>();
        services.AddSingleton<ICardService<HsrCharacterInformation>, HsrCharacterCardService>();
        services.RegisterAsyncInitializableFor<ICardService<HsrCharacterInformation>, HsrCharacterCardService>();

        services.AddTransient<IApplicationService<HsrCharListApplicationContext>, HsrCharListApplicationService>();
        services.AddSingleton<ICardService<IEnumerable<HsrCharacterInformation>>, HsrCharListCardService>();

        services.AddTransient<IApplicationService<HsrEndGameApplicationContext>, HsrEndGameApplicationService>();
        services.AddSingleton<ICardService<HsrEndInformation>, HsrEndGameCardService>();
        services.RegisterAsyncInitializableFor<ICardService<HsrEndInformation>,
            HsrEndGameCardService>();

        services.AddTransient<IApplicationService<HsrMemoryApplicationContext>, HsrMemoryApplicationService>();
        services.AddSingleton<ICardService<HsrMemoryInformation>, HsrMemoryCardService>();
        services.RegisterAsyncInitializableFor<ICardService<HsrMemoryInformation>, HsrMemoryCardService>();

        services.AddTransient<IApplicationService<HsrAnomalyApplicationContext>, HsrAnomalyApplicationService>();
        services.AddSingleton<ICardService<HsrAnomalyInformation>, HsrAnomalyCardService>();
        services.RegisterAsyncInitializableFor<ICardService<HsrAnomalyInformation>, HsrAnomalyCardService>();

        services
            .AddTransient<IApplicationService<HsrRealTimeNotesApplicationContext>,
                HsrRealTimeNotesApplicationService>();

        return services;
    }
}
