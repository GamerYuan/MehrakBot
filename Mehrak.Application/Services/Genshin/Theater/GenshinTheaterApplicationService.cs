using Mehrak.Application.Builders;
using Mehrak.Application.Services.Common;
using Mehrak.Application.Services.Genshin.Types;
using Mehrak.Application.Utility;
using Mehrak.Domain.Common;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Genshin.Types;
using Microsoft.Extensions.Logging;

namespace Mehrak.Application.Services.Genshin.Theater;

public class GenshinTheaterApplicationService : BaseApplicationService<GenshinTheaterApplicationContext>
{
    private readonly ICardService<GenshinEndGameGenerationContext<GenshinTheaterInformation>, GenshinTheaterInformation> m_CardService;
    private readonly IApiService<GenshinTheaterInformation, BaseHoYoApiContext> m_ApiService;
    private readonly ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, CharacterApiContext> m_CharacterApiService;
    private readonly IImageUpdaterService m_ImageUpdaterService;

    public GenshinTheaterApplicationService(
        ICardService<GenshinEndGameGenerationContext<GenshinTheaterInformation>, GenshinTheaterInformation> cardService,
        IApiService<GenshinTheaterInformation, BaseHoYoApiContext> apiService,
        ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, CharacterApiContext> characterApiService,
        IImageUpdaterService imageUpdaterService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        ILogger<GenshinTheaterApplicationService> logger) : base(gameRoleApi, logger)
    {
        m_CardService = cardService;
        m_ApiService = apiService;
        m_CharacterApiService = characterApiService;
        m_ImageUpdaterService = imageUpdaterService;
    }

    public override async Task<CommandResult> ExecuteAsync(GenshinTheaterApplicationContext context)
    {
        try
        {
            var region = context.Server.ToRegion();

            var profile = await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.Genshin, region);

            if (profile == null)
            {
                Logger.LogWarning("No profile found for user {UserId}", context.UserId);
                return CommandResult.Failure("Invalid HoYoLAB UID or Cookies. Please authenticate again");
            }

            var gameUid = profile.GameUid;

            var theaterDataResult = await m_ApiService.GetAsync(new(context.UserId, context.LtUid, context.LToken, gameUid, region));
            if (!theaterDataResult.IsSuccess)
            {
                Logger.LogWarning("Failed to fetch Theater information for gameUid: {GameUid}, region: {Server}, error: {Error}",
                    profile.GameUid, context.Server, theaterDataResult.ErrorMessage);
                return CommandResult.Failure(theaterDataResult.ErrorMessage);
            }

            var theaterData = theaterDataResult.Data;

            var updateImageTask = theaterData.Detail.RoundsData.SelectMany(x => x.Avatars).DistinctBy(x => x.AvatarId)
                .Select(async x => await m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(), ImageProcessors.AvatarProcessor));
            var sideAvatarTask =
                ((ItRankAvatar[])
                [
                    theaterData.Detail.FightStatistic.MaxDamageAvatar,
                    theaterData.Detail.FightStatistic.MaxDefeatAvatar,
                    theaterData.Detail.FightStatistic.MaxTakeDamageAvatar
                ]).DistinctBy(x => x.AvatarId)
                .Select(async x =>
                    await m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(), new ImageProcessorBuilder().Resize(0, 150).Build()));
            var buffTask = theaterData.Detail.RoundsData.SelectMany(x => x.SplendourBuff!.Buffs)
                .DistinctBy(x => x.Name)
                .Select(async x => await m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(),
                    new ImageProcessorBuilder().Build()));

            var charListResponse = await m_CharacterApiService.GetAllCharactersAsync(new(context.UserId, context.LtUid, context.LToken, gameUid, region));

            if (!charListResponse.IsSuccess || !charListResponse.Data.Any())
            {
                Logger.LogWarning("Failed to fetch character data for gameUid: {GameUid}, region: {Server}, error: {Error}",
                    profile.GameUid, context.Server, charListResponse.ErrorMessage);
                return CommandResult.Failure("An error occurred while fetching character data");
            }

            var charList = charListResponse.Data.ToList();

            var constMap = charList.ToDictionary(x => x.Id!.Value, x => x.ActivedConstellationNum!.Value);

            await Task.WhenAll(updateImageTask.Concat(sideAvatarTask).Concat(buffTask));

            var card = await m_CardService.GetCardAsync(new GenshinEndGameGenerationContext<GenshinTheaterInformation>(
                context.UserId,
                0,
                theaterData,
                context.Server,
                profile,
                constMap));

            return CommandResult.Success(
                $"<@{context.UserId}>'s Imaginarium Theater Summary",
                $"Cycle start: <t:{theaterData.Schedule.StartTime}:f>\nCycle end: <t:{theaterData.Schedule.EndTime}:f>",
                $"-# Information may be inaccurate due to API limitations. Please check in-game for the most accurate data.",
                [new("theater_card.jpg", card)]);
        }
        catch (CommandException e)
        {
            Logger.LogError(e, "Failed to get Imaginarium Theater card for user {UserId}",
                context.UserId);
            return CommandResult.Failure(e.Message);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Failed to get Imaginarium Theater card for user {UserId}",
                context.UserId);
            return CommandResult.Failure(e.Message);
        }
    }
}
