#region

using System.Text.Json.Nodes;
using Mehrak.Domain.Character;
using Mehrak.Domain.Character.Models;
using Mehrak.Domain.Image;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Services;
using Mehrak.Domain.User.Models;
using Mehrak.GameApi.CodeRedeem;
using Mehrak.GameApi.DailyCheckIn;
using Mehrak.GameApi.GameRecord;
using Mehrak.GameApi.GameRole;
using Mehrak.GameApi.Genshin;
using Mehrak.GameApi.Genshin.Types;
using Mehrak.GameApi.Hi3;
using Mehrak.GameApi.Hi3.Types;
using Mehrak.GameApi.Hsr;
using Mehrak.GameApi.Hsr.Types;
using Mehrak.GameApi.Shared;
using Mehrak.GameApi.Shared.Types;
using Mehrak.GameApi.Wiki;
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
        services.AddSingleton<GameRoleApiService>();
        services.AddSingleton<IApiService<GameProfileDto, GameRoleApiContext>>(sp => sp.GetRequiredService<GameRoleApiService>());
        services.AddSingleton<IApiService<JsonNode, WikiApiContext>, WikiApiService>();
        services.AddSingleton<IImageUpdaterService, ImageUpdaterService>();

        // Genshin services
        services.AddSingleton<IApiService<GameApiResponse<GenshinAbyssInformation>, BaseHoYoApiContext>, GenshinAbyssApiService>();
        services
            .AddSingleton<ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, GenshinCharacterApiContext>,
                GenshinCharacterApiService>();
        services
            .AddSingleton<IApiService<GenshinRealTimeNotesData, BaseHoYoApiContext>, GenshinRealTimeNotesApiService>();
        services.AddSingleton<IApiService<GameApiResponse<GenshinStygianInformation>, BaseHoYoApiContext>, GenshinStygianApiService>();
        services.AddSingleton<IApiService<GameApiResponse<GenshinTheaterResponseData>, BaseHoYoApiContext>, GenshinTheaterApiService>();

        // Honkai: Star Rail services
        services
            .AddSingleton<ICharacterApiService<HsrBasicCharacterData, HsrBasicCharacterData, CharacterApiContext>,
                HsrCharacterApiService>();
        services.AddSingleton<IApiService<GameApiResponse<HsrMemoryInformation>, BaseHoYoApiContext>, HsrMemoryApiService>();
        services.AddSingleton<IApiService<GameApiResponse<HsrEndInformation>, HsrEndGameApiContext>, HsrEndGameApiService>();
        services.AddSingleton<IApiService<HsrRealTimeNotesData, BaseHoYoApiContext>, HsrRealTimeNotesApiService>();
        services.AddSingleton<IApiService<GameApiResponse<HsrAnomalyInformation>, BaseHoYoApiContext>, HsrAnomalyApiService>();

        // Zenless Zone Zero services
        services.AddSingleton<IApiService<GameApiResponse<ZzzAssaultData>, BaseHoYoApiContext>, ZzzAssaultApiService>();
        services
            .AddSingleton<ICharacterApiService<ZzzBasicAvatarData, ZzzFullAvatarData, CharacterApiContext>,
                ZzzCharacterApiService>();
        services.AddSingleton<IApiService<GameApiResponse<ZzzDefenseDataV2>, BaseHoYoApiContext>, ZzzDefenseApiService>();
        services.AddSingleton<IApiService<ZzzRealTimeNotesData, BaseHoYoApiContext>, ZzzRealTimeNotesApiService>();
        services.AddSingleton<IApiService<IEnumerable<ZzzBuddyData>, BaseHoYoApiContext>, ZzzBuddyApiService>();
        services.AddSingleton<IApiService<GameApiResponse<ZzzTowerData>, BaseHoYoApiContext>, ZzzTowerApiService>();
        services
            .AddSingleton<IApiService<ZzzCharacterEntryPageList, ZzzCharacterEntryPageApiContext>,
                ZzzCharacterEntryPageApiService>();

        // HI3 services
        services.AddSingleton<
            ICharacterApiService<Hi3CharacterDetail, Hi3CharacterDetail, CharacterApiContext>, Hi3CharacterApiService>();

        return services;
    }
}
