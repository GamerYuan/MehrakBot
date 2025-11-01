#region

using System.Text.Json.Nodes;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Genshin;
using Mehrak.GameApi.Genshin.Types;
using Mehrak.GameApi.Hsr;
using Mehrak.GameApi.Hsr.Types;
using Mehrak.GameApi.Zzz;
using Mehrak.GameApi.Zzz.Types;
using Microsoft.Extensions.DependencyInjection;

#endregion

namespace Mehrak.GameApi;

public static class GameApiServiceCollectionExtension
{
    public static IServiceCollection AddGameApiServices(this IServiceCollection services)
    {
        // Common services
        services.AddSingleton<IApiService<CodeRedeemResult, CodeRedeemApiContext>, CodeRedeemApiService>();
        services.AddSingleton<IApiService<CheckInStatus, CheckInApiContext>, DailyCheckInApiService>();
        services.AddSingleton<IApiService<IEnumerable<GameRecordDto>, GameRecordApiContext>, GameRecordApiService>();
        services.AddSingleton<IApiService<GameProfileDto, GameRoleApiContext>, GameRoleApiService>();
        services.AddSingleton<IApiService<JsonNode, WikiApiContext>, WikiApiService>();
        services.AddSingleton<IImageUpdaterService, ImageUpdaterService>();

        // Genshin services
        services.AddSingleton<IApiService<GenshinAbyssInformation, BaseHoYoApiContext>, GenshinAbyssApiService>();
        services
            .AddSingleton<ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, CharacterApiContext>,
                GenshinCharacterApiService>();
        services
            .AddSingleton<IApiService<GenshinRealTimeNotesData, BaseHoYoApiContext>, GenshinRealTimeNotesApiService>();
        services.AddSingleton<IApiService<GenshinStygianInformation, BaseHoYoApiContext>, GenshinStygianApiService>();
        services.AddSingleton<IApiService<GenshinTheaterInformation, BaseHoYoApiContext>, GenshinTheaterApiService>();

        // Honkai: Star Rail services
        services
            .AddSingleton<ICharacterApiService<HsrBasicCharacterData, HsrCharacterInformation, CharacterApiContext>,
                HsrCharacterApiService>();
        services.AddSingleton<IApiService<HsrMemoryInformation, BaseHoYoApiContext>, HsrMemoryApiService>();
        services.AddSingleton<IApiService<HsrEndInformation, HsrEndGameApiContext>, HsrEndGameApiService>();
        services.AddSingleton<IApiService<HsrRealTimeNotesData, BaseHoYoApiContext>, HsrRealTimeNotesApiService>();

        // Zenless Zone Zero services
        services.AddSingleton<IApiService<ZzzAssaultData, BaseHoYoApiContext>, ZzzAssaultApiService>();
        services
            .AddSingleton<ICharacterApiService<ZzzBasicAvatarData, ZzzFullAvatarData, CharacterApiContext>,
                ZzzCharacterApiService>();
        services.AddSingleton<IApiService<ZzzDefenseData, BaseHoYoApiContext>, ZzzDefenseApiService>();
        services.AddSingleton<IApiService<ZzzRealTimeNotesData, BaseHoYoApiContext>, ZzzRealTimeNotesApiService>();

        return services;
    }
}