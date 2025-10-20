using Mehrak.Application.Services.Hsr.Character;
using Mehrak.Application.Services.Hsr.CharList;
using Mehrak.Application.Services.Hsr.EndGame;
using Mehrak.Application.Services.Hsr.Memory;
using Mehrak.Application.Services.Hsr.RealTimeNotes;
using Mehrak.Application.Services.Hsr.Types;
using Mehrak.Domain;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Hsr.Types;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddSingleton<ICardService<HsrEndGameGenerationContext, HsrEndInformation>,
            HsrEndGameCardService>();
        services.RegisterAsyncInitializableFor<ICardService<HsrEndGameGenerationContext, HsrEndInformation>,
            HsrEndGameCardService>();

        services.AddTransient<IApplicationService<HsrMemoryApplicationContext>, HsrMemoryApplicationService>();
        services.AddSingleton<ICardService<HsrMemoryInformation>, HsrMemoryCardService>();
        services.RegisterAsyncInitializableFor<ICardService<HsrMemoryInformation>, HsrMemoryCardService>();

        services.AddTransient<IApplicationService<HsrRealTimeNotesApplicationContext>, HsrRealTimeNotesApplicationService>();

        return services;
    }
}
