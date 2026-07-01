#region

using System.Text.Json;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Builders;
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
using Mehrak.GameApi.Hsr.Types;
using Mehrak.GameApi.Shared.Types;
using Mehrak.Infrastructure.User;

#endregion

namespace Mehrak.Application.Hsr.CharList;

public class HsrCharListApplicationService : BaseAttachmentApplicationService
{
    private readonly ICardService<IEnumerable<HsrCharacterInformation>> m_CardService;
    private readonly IImageUpdaterService m_ImageUpdaterService;

    private readonly ICharacterApiService<HsrBasicCharacterData, HsrBasicCharacterData, CharacterApiContext>
        m_CharacterApi;
    private readonly ICharacterCacheService m_CharacterCache;


    protected override string CommandName => "CharList";
    protected override bool RequiresLevel => true;
    protected override string CardName => "Character List";
    public HsrCharListApplicationService(
        ICardService<IEnumerable<HsrCharacterInformation>> cardService,
        IImageUpdaterService imageUpdaterService,
        ICharacterApiService<HsrBasicCharacterData, HsrBasicCharacterData, CharacterApiContext> characterApi,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        UserDbContext userContext,
        ICharacterCacheService characterCache,
        IAttachmentStorageService attachmentStorageService,
        ILogger<HsrCharListApplicationService> logger)
        : base(gameRoleApi, userContext, attachmentStorageService, logger)
    {
        m_CardService = cardService;
        m_ImageUpdaterService = imageUpdaterService;
        m_CharacterApi = characterApi;
        m_CharacterCache = characterCache;
    }

    protected override async Task<CommandResult> ExecuteCommandAsync(IApplicationContext context, CancellationToken cancellationToken = default)
    {
        var server = Enum.Parse<Server>(context.GetParameter("server")!);
        var region = server.ToRegion();

        var profileResult = await GetOrFetchGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.HonkaiStarRail,
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

        var gameUid = profile.GameUid;

        var charResponse = await
            m_CharacterApi.GetAllCharactersAsync(new CharacterApiContext(context.UserId, context.LtUid,
                context.LToken, gameUid, region), cancellationToken);

        if (!charResponse.IsSuccess)
        {
            if (charResponse.StatusCode == StatusCode.Cancelled)
                throw new OperationCanceledException(charResponse.ErrorMessage ?? "Cancelled");
            if (charResponse.StatusCode == StatusCode.Timeout)
                return CommandResult.Failure(CommandFailureReason.Timeout, ResponseMessage.TimeoutError);
            Logger.LogError(LogMessage.ApiError, "CharList", context.UserId, gameUid, charResponse);
            return CommandResult.Failure(CommandFailureReason.ApiError,
                string.Format(ResponseMessage.ApiError, "Character List"));
        }

        var characterList = charResponse.Data.FirstOrDefault()?.AvatarList ?? [];
        _ = m_CharacterCache.UpsertCharacters(Game.HonkaiStarRail,
            characterList.Select(x => new CharacterUpsertEntry(x.Name, x.Id)));

        var fileName = GetFileName(JsonSerializer.Serialize(characterList), "jpg", gameUid);
        if (await AttachmentExistsAsync(fileName))
        {
            return CommandResult.Success([
                new CommandText($"<@{context.UserId}>")
                    , new CommandAttachment(fileName)
            ]);
        }

        var avatarTask = characterList.Select(x => m_ImageUpdaterService
            .UpdateImageAsync(x.ToAvatarImageData(), ImageProcessors.AvatarProcessor, cancellationToken));
        var weaponTask =
            characterList.Where(x => x.Equip is not null).Select(x =>
                m_ImageUpdaterService.UpdateImageAsync(x.Equip!.ToIconImageData(),
                    new ImageProcessorBuilder().Resize(150, 0).Build(), cancellationToken));

        var completed = await Task.WhenAll(avatarTask.Concat(weaponTask));

        if (completed.Any(x => !x))
        {
            Logger.LogError(LogMessage.ImageUpdateError, "CharList", context.UserId,
                JsonSerializer.Serialize(characterList));
            return CommandResult.Failure(CommandFailureReason.ApiError, ResponseMessage.ImageUpdateError);
        }

        var cardContext = new BaseCardGenerationContext<IEnumerable<HsrCharacterInformation>>(
                context.UserId, characterList, profile);
        cardContext.SetParameter("server", server);

        await using var card = await m_CardService.GetCardAsync(cardContext);

        if (!await StoreAttachmentAsync(context.UserId, fileName, card))
        {
            Logger.LogError(LogMessage.AttachmentStoreError, fileName, context.UserId);
            return CommandResult.Failure(CommandFailureReason.BotError,
                ResponseMessage.AttachmentStoreError);
        }

        return CommandResult.Success([
            new CommandText($"<@{context.UserId}>")
                , new CommandAttachment(fileName)
        ]);
    }
}
