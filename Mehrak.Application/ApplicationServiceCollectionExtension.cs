using Mehrak.Application.Services.Genshin.Abyss;
using Mehrak.Application.Services.Genshin.Character;
using Mehrak.Application.Services.Genshin.CharList;
using Mehrak.Application.Services.Genshin.RealTimeNotes;
using Mehrak.Application.Services.Genshin.Stygian;
using Mehrak.Application.Services.Genshin.Theater;
using Mehrak.Application.Services.Genshin.Types;
using Mehrak.Domain;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Genshin.Types;
using Microsoft.Extensions.DependencyInjection;

namespace Mehrak.Application;

public static class ApplicationServiceCollectionExtension
{
    public static IServiceCollection AddGenshinApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<ICardService<GenshinEndGameGenerationContext<GenshinAbyssInformation>, GenshinAbyssInformation>,
            GenshinAbyssCardService>();
        services.RegisterAsyncInitializableFor<
            ICardService<GenshinEndGameGenerationContext<GenshinAbyssInformation>, GenshinAbyssInformation>, GenshinAbyssCardService>();
        services.AddTransient<IApplicationService<GenshinAbyssApplicationContext>, GenshinAbyssApplicationService>();

        services.AddSingleton<ICardService<GenshinCharacterInformation>, GenshinCharacterCardService>();
        services.RegisterAsyncInitializableFor<ICardService<GenshinCharacterInformation>, GenshinCharacterCardService>();
        services.AddTransient<IApplicationService<GenshinCharacterApplicationContext>, GenshinCharacterApplicationService>();

        services.AddSingleton<ICardService<IEnumerable<GenshinBasicCharacterData>>, GenshinCharListCardService>();
        services.AddTransient<IApplicationService<GenshinCharListApplicationContext>, GenshinCharListApplicationService>();

        services.AddSingleton<ICardService<StygianData>, GenshinStygianCardService>();
        services.RegisterAsyncInitializableFor<ICardService<StygianData>, GenshinStygianCardService>();
        services.AddTransient<IApplicationService<GenshinStygianApplicationContext>, GenshinStygianApplicationService>();

        services.AddSingleton<ICardService<GenshinEndGameGenerationContext<GenshinTheaterInformation>, GenshinTheaterInformation>,
            GenshinTheaterCardService>();
        services.RegisterAsyncInitializableFor<ICardService<GenshinEndGameGenerationContext<GenshinTheaterInformation>,
            GenshinTheaterInformation>, GenshinTheaterCardService>();
        services.AddTransient<IApplicationService<GenshinTheaterApplicationContext>, GenshinTheaterApplicationService>();

        services.AddTransient<IApplicationService<GenshinRealTimeNotesApplicationContext>, GenshinRealTimeNotesApplicationService>();

        return services;
    }
}
