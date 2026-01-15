#region

using Mehrak.Application.Services.Genshin.Abyss;
using Mehrak.Application.Services.Genshin.Character;
using Mehrak.Application.Services.Genshin.CharList;
using Mehrak.Application.Services.Genshin.RealTimeNotes;
using Mehrak.Application.Services.Genshin.Stygian;
using Mehrak.Application.Services.Genshin.Theater;
using Mehrak.Domain;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Genshin.Types;
using Microsoft.Extensions.DependencyInjection;

#endregion

namespace Mehrak.Application.Services.Genshin;

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
        services.AddTransient<IApplicationService<GenshinAbyssApplicationContext>, GenshinAbyssApplicationService>();

        services.AddSingleton<ICardService<GenshinCharacterInformation>, GenshinCharacterCardService>();
        services
            .RegisterAsyncInitializableFor<ICardService<GenshinCharacterInformation>, GenshinCharacterCardService>();
        services
            .AddTransient<IApplicationService<GenshinCharacterApplicationContext>,
                GenshinCharacterApplicationService>();

        services.AddSingleton<ICardService<IEnumerable<GenshinBasicCharacterData>>, GenshinCharListCardService>();
        services
            .AddTransient<IApplicationService<GenshinCharListApplicationContext>, GenshinCharListApplicationService>();

        services.AddSingleton<ICardService<StygianData>, GenshinStygianCardService>();
        services.RegisterAsyncInitializableFor<ICardService<StygianData>, GenshinStygianCardService>();
        services
            .AddTransient<IApplicationService<GenshinStygianApplicationContext>, GenshinStygianApplicationService>();

        services
            .AddSingleton<ICardService<GenshinTheaterInformation>,
                GenshinTheaterCardService>();
        services.RegisterAsyncInitializableFor<ICardService<GenshinTheaterInformation>, GenshinTheaterCardService>();
        services
            .AddTransient<IApplicationService<GenshinTheaterApplicationContext>, GenshinTheaterApplicationService>();

        services
            .AddTransient<IApplicationService<GenshinRealTimeNotesApplicationContext>,
                GenshinRealTimeNotesApplicationService>();

        return services;
    }
}
