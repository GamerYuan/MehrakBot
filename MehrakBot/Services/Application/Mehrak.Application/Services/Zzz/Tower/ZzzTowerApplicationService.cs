using System.Text.Json;
using Mehrak.Application.Services.Abstractions;
using Mehrak.Application.Services.Common;
using Mehrak.Application.Services.Common.Types;
using Mehrak.Application.Utility;
using Mehrak.Domain.Common;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Zzz.Types;
using Mehrak.Infrastructure.Context;

namespace Mehrak.Application.Services.Zzz.Tower;

public class ZzzTowerApplicationService : BaseAttachmentApplicationService
{
    private readonly ICardService<ZzzTowerData> m_CardService;
    private readonly IApiService<ZzzTowerData, BaseHoYoApiContext> m_ApiService;
    private readonly ICharacterApiService<ZzzBasicAvatarData, ZzzFullAvatarData, CharacterApiContext> m_CharacterApi;
    private readonly IImageUpdaterService m_ImageUpdaterService;

    public ZzzTowerApplicationService(
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        UserDbContext userContext,
        IAttachmentStorageService attachmentStorageService,
        ICardService<ZzzTowerData> cardService,
        IApiService<ZzzTowerData, BaseHoYoApiContext> apiService,
        ICharacterApiService<ZzzBasicAvatarData, ZzzFullAvatarData, CharacterApiContext> characterApi,
        IImageUpdaterService imageUpdaterService,
        ILogger<ZzzTowerApplicationService> logger)
        : base(gameRoleApi, userContext, attachmentStorageService, logger)
    {
        m_CardService = cardService;
        m_ApiService = apiService;
        m_CharacterApi = characterApi;
        m_ImageUpdaterService = imageUpdaterService;
    }

    public override async Task<CommandResult> ExecuteAsync(IApplicationContext context)
    {
        try
        {
            var server = Enum.Parse<Server>(context.GetParameter("server")!);
            var region = server.ToRegion();

            var profile = await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.ZenlessZoneZero,
                region);

            if (profile == null)
            {
                Logger.LogWarning(LogMessage.InvalidLogin, context.UserId);
                return CommandResult.Failure(CommandFailureReason.AuthError, ResponseMessage.AuthError);
            }

            await UpdateGameUidAsync(context.UserId, context.LtUid, Game.ZenlessZoneZero, profile.GameUid, server.ToString());

            var gameUid = profile.GameUid;

            var towerResponse =
                await m_ApiService.GetAsync(new BaseHoYoApiContext(context.UserId, context.LtUid, context.LToken,
                    gameUid, region));
            if (!towerResponse.IsSuccess)
            {
                Logger.LogError(LogMessage.ApiError, "Simulated Battle Trial", context.UserId, gameUid, towerResponse);
                return CommandResult.Failure(CommandFailureReason.ApiError,
                    string.Format(ResponseMessage.ApiError, "Simulated Battle Trial data"));
            }

            var towerData = towerResponse.Data;

            if (towerData == null)
            {
                Logger.LogInformation(LogMessage.NoClearRecords, "Simulated Battle Trial", context.UserId, gameUid);
                return CommandResult.Success(
                    [new CommandText(string.Format(ResponseMessage.NoClearRecords, "Simulated Battle Trial"))],
                    isEphemeral: true);
            }


            var characterResponse = await m_CharacterApi.GetAllCharactersAsync(
                new CharacterApiContext(context.UserId, context.LtUid, context.LToken, gameUid, region));

            if (!characterResponse.IsSuccess)
            {
                Logger.LogError(LogMessage.ApiError, "Character List", context.UserId, gameUid, towerResponse);
                return CommandResult.Failure(CommandFailureReason.ApiError,
                    string.Format(ResponseMessage.ApiError, "Character List data"));
            }

            var fileName = GetFileName(JsonSerializer.Serialize(towerData), "jpg", gameUid);
            if (await AttachmentExistsAsync(fileName))
            {
                return CommandResult.Success([
                        new CommandText($"<@{context.UserId}>'s Simulated Battle Trial Summary", CommandText.TextType.Header3),
                        new CommandAttachment(fileName),
                        new CommandText(ResponseMessage.ApiLimitationFooter, CommandText.TextType.Footer)
                    ],
                    true);
            }

            var avatarImageTask = towerData.DisplayAvatarRankList
                .DistinctBy(x => x.AvatarId)
                .Select(avatar =>
                    m_ImageUpdaterService.UpdateImageAsync(avatar.ToImageData(), ImageProcessors.AvatarProcessor));

            var completed =
                await Task.WhenAll(avatarImageTask);

            if (completed.Any(x => !x))
            {
                Logger.LogError(LogMessage.ImageUpdateError, "Simulated Battle Trial", context.UserId,
                    JsonSerializer.Serialize(towerData));
                return CommandResult.Failure(CommandFailureReason.ApiError, ResponseMessage.ImageUpdateError);
            }

            var rankMap = characterResponse.Data.ToDictionary(x => x.Id, x => x.Rank);

            var cardContext = new BaseCardGenerationContext<ZzzTowerData>(context.UserId, towerData, profile);
            cardContext.SetParameter("server", server);
            cardContext.SetParameter("rankMap", rankMap);

            await using var card = await m_CardService.GetCardAsync(cardContext);

            if (!await StoreAttachmentAsync(context.UserId, fileName, card))
            {
                Logger.LogError(LogMessage.AttachmentStoreError, fileName, context.UserId);
                return CommandResult.Failure(CommandFailureReason.BotError, ResponseMessage.AttachmentStoreError);
            }

            return CommandResult.Success([
                    new CommandText($"<@{context.UserId}>'s Simulated Battle Trial Summary", CommandText.TextType.Header3),
                    new CommandAttachment(fileName),
                    new CommandText(ResponseMessage.ApiLimitationFooter, CommandText.TextType.Footer)
                ],
                true);
        }
        catch (CommandException e)
        {
            Logger.LogError(e, LogMessage.UnknownError, "Simulated Battle Trial", context.UserId, e.Message);
            return CommandResult.Failure(CommandFailureReason.BotError,
                string.Format(ResponseMessage.CardGenError, "Simulated Battle Trial"));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, LogMessage.UnknownError, "Simulated Battle Trial", context.UserId, ex.Message);
            return CommandResult.Failure(CommandFailureReason.Unknown, ResponseMessage.UnknownError);
        }
    }
}
