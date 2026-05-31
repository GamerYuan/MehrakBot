using System.Text.Json;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Services;
using Mehrak.Application.Shared.Services.Types;
using Mehrak.Application.Shared.Utility;
using Mehrak.Domain.Card;
using Mehrak.Domain.Character;
using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Image;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.Domain.User.Models;
using Mehrak.GameApi.GameRole;
using Mehrak.GameApi.Shared.Types;
using Mehrak.GameApi.Zzz.Types;
using Mehrak.Infrastructure.User;

namespace Mehrak.Application.Zzz.Tower;

public class ZzzTowerApplicationService : BaseAttachmentApplicationService
{
    private readonly ICardService<ZzzTowerData> m_CardService;
    private readonly IApiService<ZzzTowerData, BaseHoYoApiContext> m_ApiService;
    private readonly ICharacterApiService<ZzzBasicAvatarData, ZzzFullAvatarData, CharacterApiContext> m_CharacterApi;
    private readonly IImageUpdaterService m_ImageUpdaterService;


    protected override string CommandName => "Simulated Battle Trial";
    protected override string CardName => "Simulated Battle Trial";
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

    protected override async Task<CommandResult> ExecuteCommandAsync(IApplicationContext context, CancellationToken cancellationToken = default)
    {
        var server = Enum.Parse<Server>(context.GetParameter("server")!);
        var region = server.ToRegion();

        var profileResult = await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.ZenlessZoneZero,
            region, cancellationToken);
        if (!profileResult.IsSuccess)
        {
            if (profileResult.StatusCode == StatusCode.Cancelled)
                throw new OperationCanceledException(profileResult.ErrorMessage ?? "Cancelled");
            if (profileResult.StatusCode == StatusCode.Timeout)
                return CommandResult.Failure(CommandFailureReason.Timeout, ResponseMessage.TimeoutError);
            Logger.LogWarning(LogMessage.InvalidLogin, context.UserId);
            return CommandResult.Failure(CommandFailureReason.AuthError, ResponseMessage.AuthError);
        }
        var profile = profileResult.Data;

        await UpdateGameUidAsync(context.UserId, context.LtUid, Game.ZenlessZoneZero, profile.GameUid, server.ToString(), cancellationToken);

        var gameUid = profile.GameUid;

        var towerResponse =
            await m_ApiService.GetAsync(new BaseHoYoApiContext(context.UserId, context.LtUid, context.LToken,
                gameUid, region), cancellationToken);
        if (!towerResponse.IsSuccess)
        {
            if (towerResponse.StatusCode == StatusCode.Cancelled)
                throw new OperationCanceledException(towerResponse.ErrorMessage ?? "Cancelled");
            if (towerResponse.StatusCode == StatusCode.Timeout)
                return CommandResult.Failure(CommandFailureReason.Timeout, ResponseMessage.TimeoutError);
            Logger.LogError(LogMessage.ApiError, "Simulated Battle Trial", context.UserId, gameUid, towerResponse);
            return CommandResult.Failure(CommandFailureReason.ApiError,
                string.Format(ResponseMessage.ApiError, "Simulated Battle Trial data"));
        }

        var towerData = towerResponse.Data;

        if (towerData.DisplayAvatarRankList.Count == 0)
        {
            Logger.LogInformation(LogMessage.NoClearRecords, "Simulated Battle Trial", context.UserId, gameUid);
            return CommandResult.Success(
                [new CommandText(string.Format(ResponseMessage.NoClearRecords, "Simulated Battle Trial"))],
                isEphemeral: true);
        }


        var characterResponse = await m_CharacterApi.GetAllCharactersAsync(
            new CharacterApiContext(context.UserId, context.LtUid, context.LToken, gameUid, region), cancellationToken);

        if (!characterResponse.IsSuccess)
        {
            if (characterResponse.StatusCode == StatusCode.Cancelled)
                throw new OperationCanceledException(characterResponse.ErrorMessage ?? "Cancelled");
            if (characterResponse.StatusCode == StatusCode.Timeout)
                return CommandResult.Failure(CommandFailureReason.Timeout, ResponseMessage.TimeoutError);
            Logger.LogError(LogMessage.ApiError, "Character List", context.UserId, gameUid, characterResponse);
            return CommandResult.Failure(CommandFailureReason.ApiError,
                string.Format(ResponseMessage.ApiError, "Character List data"));
        }

        var charMap = characterResponse.Data.ToDictionary(x => x.Id, x => (x.Level, x.Rank));

        foreach (var avatar in towerData.DisplayAvatarRankList)
        {
            if (!charMap.ContainsKey(avatar.AvatarId))
            {
                Logger.LogError(LogMessage.ApiError, "Character List", context.UserId, gameUid, characterResponse);
                return CommandResult.Failure(CommandFailureReason.ApiError,
                    string.Format(ResponseMessage.ApiError, "Character List data"));
            }
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
                m_ImageUpdaterService.UpdateImageAsync(avatar.ToImageData(), ImageProcessors.AvatarProcessor, cancellationToken));

        var completed =
            await Task.WhenAll(avatarImageTask);

        if (completed.Any(x => !x))
        {
            Logger.LogError(LogMessage.ImageUpdateError, "Simulated Battle Trial", context.UserId,
                JsonSerializer.Serialize(towerData));
            return CommandResult.Failure(CommandFailureReason.ApiError, ResponseMessage.ImageUpdateError);
        }

        var cardContext = new BaseCardGenerationContext<ZzzTowerData>(context.UserId, towerData, profile);
        cardContext.SetParameter("server", server);
        cardContext.SetParameter("charMap", charMap);

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
}
