#region

using System.Text.Json;
using Mehrak.Application.Builders;
using Mehrak.Application.Services.Abstractions;
using Mehrak.Application.Services.Common;
using Mehrak.Application.Services.Common.Types;
using Mehrak.Application.Utility;
using Mehrak.Domain.Common;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Genshin.Types;
using Mehrak.Infrastructure.Context;

#endregion

namespace Mehrak.Application.Services.Genshin.Theater;

public class GenshinTheaterApplicationService : BaseAttachmentApplicationService
{
    private readonly ICardService<GenshinTheaterInformation>
        m_CardService;

    private readonly IApiService<GenshinTheaterInformation, BaseHoYoApiContext> m_ApiService;

    private readonly ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, GenshinCharacterApiContext>
        m_CharacterApiService;

    private readonly IImageUpdaterService m_ImageUpdaterService;

    public GenshinTheaterApplicationService(
        ICardService<GenshinTheaterInformation> cardService,
        IApiService<GenshinTheaterInformation, BaseHoYoApiContext> apiService,
        ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, GenshinCharacterApiContext>
            characterApiService,
        IImageUpdaterService imageUpdaterService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        UserDbContext userContext,
        IAttachmentStorageService attachmentStorageService,
        ILogger<GenshinTheaterApplicationService> logger)
        : base(gameRoleApi, userContext, attachmentStorageService, logger)
    {
        m_CardService = cardService;
        m_ApiService = apiService;
        m_CharacterApiService = characterApiService;
        m_ImageUpdaterService = imageUpdaterService;
    }

    public override async Task<CommandResult> ExecuteAsync(IApplicationContext context)
    {
        try
        {
            var server = Enum.Parse<Server>(context.GetParameter("server")!);
            var region = server.ToRegion();

            var profile =
                await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.Genshin, region);

            if (profile == null)
            {
                Logger.LogInformation(LogMessage.InvalidLogin, context.UserId);
                return CommandResult.Failure(CommandFailureReason.AuthError, ResponseMessage.AuthError);
            }

            await UpdateGameUidAsync(context.UserId, context.LtUid, Game.Genshin, profile.GameUid, server.ToString());

            var gameUid = profile.GameUid;

            var theaterDataResult =
                await m_ApiService.GetAsync(new BaseHoYoApiContext(context.UserId, context.LtUid, context.LToken,
                    gameUid, region));
            if (!theaterDataResult.IsSuccess)
            {
                if (theaterDataResult.StatusCode == StatusCode.Unauthorized)
                {
                    Logger.LogInformation("Theater is not unlocked for User {UserId} UID {GameUid}", context.UserId,
                        gameUid);
                    return CommandResult.Success([new CommandText("Imaginarium Theater is not unlocked")]);
                }

                Logger.LogError(LogMessage.ApiError, "Theater", context.UserId, gameUid, theaterDataResult);
                return CommandResult.Failure(CommandFailureReason.ApiError,
                    string.Format(ResponseMessage.ApiError, "Imaginarium Theater data"));
            }

            var theaterData = theaterDataResult.Data;

            if (!theaterData.HasDetailData)
            {
                Logger.LogInformation(LogMessage.NoClearRecords, "Theater", context.UserId, gameUid);
                return CommandResult.Success(
                    [new CommandText(string.Format(ResponseMessage.NoClearRecords, "Imaginarium Theater"))],
                    isEphemeral: true);
            }

            var filename = GetFileName(JsonSerializer.Serialize(theaterData), "jpg", profile.GameUid);
            if (await AttachmentExistsAsync(filename))
            {
                return CommandResult.Success([
                    new CommandText($"<@{context.UserId}>'s Imaginarium Theater Summary", CommandText.TextType.Header3),
                    new CommandText(
                        $"Cycle start: <t:{theaterData.Schedule.StartTime}:f>\nCycle end: <t:{theaterData.Schedule.EndTime}:f>"),
                    new CommandAttachment(filename),
                    new CommandText(ResponseMessage.ApiLimitationFooter, CommandText.TextType.Footer)
                ], true);
            }

            var updateImageTask = theaterData.Detail.RoundsData.SelectMany(x => x.Avatars).DistinctBy(x => x.AvatarId)
                .Select(async x =>
                    await m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(), ImageProcessors.AvatarProcessor));
            var sideAvatarTask =
                ((ItRankAvatar[])
                [
                    theaterData.Detail.FightStatistic.MaxDamageAvatar,
                    theaterData.Detail.FightStatistic.MaxDefeatAvatar,
                    theaterData.Detail.FightStatistic.MaxTakeDamageAvatar
                ]).DistinctBy(x => x.AvatarId)
                .Select(async x =>
                    await m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(),
                        new ImageProcessorBuilder().Resize(0, 150).Build()));
            var buffTask = theaterData.Detail.RoundsData.SelectMany(x => x.SplendourBuff!.Buffs)
                .DistinctBy(x => x.Name)
                .Select(async x => await m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(),
                    new ImageProcessorBuilder().Build()));

            var charListResponse = await m_CharacterApiService.GetAllCharactersAsync(
                new GenshinCharacterApiContext(context.UserId, context.LtUid, context.LToken, gameUid, region));

            if (!charListResponse.IsSuccess || !charListResponse.Data.Any())
            {
                Logger.LogError(LogMessage.ApiError, "CharList", context.UserId, gameUid, charListResponse);
                return CommandResult.Failure(CommandFailureReason.ApiError,
                    string.Format(ResponseMessage.ApiError, "character list"));
            }

            var charList = charListResponse.Data.ToList();

            var constMap = charList.ToDictionary(x => x.Id!.Value, x => x.ActivedConstellationNum!.Value);

            var completed = await Task.WhenAll(updateImageTask.Concat(sideAvatarTask).Concat(buffTask));

            if (completed.Any(x => !x))
            {
                Logger.LogError(LogMessage.ImageUpdateError, "Theater", context.UserId,
                    JsonSerializer.Serialize(theaterData));
                return CommandResult.Failure(CommandFailureReason.ApiError, ResponseMessage.ImageUpdateError);
            }

            var cardContext = new BaseCardGenerationContext<GenshinTheaterInformation>(
                context.UserId, theaterData, profile);
            cardContext.SetParameter("constMap", constMap);
            cardContext.SetParameter("server", server);

            using var card = await m_CardService.GetCardAsync(cardContext);
            if (!await StoreAttachmentAsync(context.UserId, filename, card))
            {
                Logger.LogError(LogMessage.AttachmentStoreError, filename, context.UserId);
                return CommandResult.Failure(CommandFailureReason.BotError,
                    ResponseMessage.AttachmentStoreError);
            }

            return CommandResult.Success([
                    new CommandText($"<@{context.UserId}>'s Imaginarium Theater Summary", CommandText.TextType.Header3),
                    new CommandText(
                        $"Cycle start: <t:{theaterData.Schedule.StartTime}:f>\nCycle end: <t:{theaterData.Schedule.EndTime}:f>"),
                    new CommandAttachment(filename),
                    new CommandText(ResponseMessage.ApiLimitationFooter, CommandText.TextType.Footer)
                ],
                true);
        }
        catch (CommandException e)
        {
            Logger.LogError(e, LogMessage.UnknownError, "Theater", context.UserId, e.Message);
            return CommandResult.Failure(CommandFailureReason.BotError,
                string.Format(ResponseMessage.CardGenError, "Imaginarium Theater"));
        }
        catch (Exception e)
        {
            Logger.LogError(e, LogMessage.UnknownError, "Theater", context.UserId, e.Message);
            return CommandResult.Failure(CommandFailureReason.Unknown, ResponseMessage.UnknownError);
        }
    }
}
