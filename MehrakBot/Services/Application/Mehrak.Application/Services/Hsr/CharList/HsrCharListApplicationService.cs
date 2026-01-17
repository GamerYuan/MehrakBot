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
using Mehrak.GameApi.Hsr.Types;
using Mehrak.Infrastructure.Context;

#endregion

namespace Mehrak.Application.Services.Hsr.CharList;

public class HsrCharListApplicationService : BaseAttachmentApplicationService
{
    private readonly ICardService<IEnumerable<HsrCharacterInformation>> m_CardService;
    private readonly IImageUpdaterService m_ImageUpdaterService;

    private readonly ICharacterApiService<HsrBasicCharacterData, HsrCharacterInformation, CharacterApiContext>
        m_CharacterApi;
    private readonly ICharacterCacheService m_CharacterCache;

    public HsrCharListApplicationService(
        ICardService<IEnumerable<HsrCharacterInformation>> cardService,
        IImageUpdaterService imageUpdaterService,
        ICharacterApiService<HsrBasicCharacterData, HsrCharacterInformation, CharacterApiContext> characterApi,
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

    public override async Task<CommandResult> ExecuteAsync(IApplicationContext context)
    {
        try
        {
            var server = Enum.Parse<Server>(context.GetParameter("server")!);
            var region = server.ToRegion();

            var profile = await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.HonkaiStarRail,
                region);

            if (profile == null)
            {
                Logger.LogWarning(LogMessage.InvalidLogin, context.UserId);
                return CommandResult.Failure(CommandFailureReason.AuthError, ResponseMessage.AuthError);
            }

            await UpdateGameUidAsync(context.UserId, context.LtUid, Game.HonkaiStarRail, profile.GameUid, server.ToString());

            var gameUid = profile.GameUid;

            var charResponse = await
                m_CharacterApi.GetAllCharactersAsync(new CharacterApiContext(context.UserId, context.LtUid,
                    context.LToken, gameUid, region));

            if (!charResponse.IsSuccess)
            {
                Logger.LogError(LogMessage.ApiError, "CharList", context.UserId, gameUid, charResponse);
                return CommandResult.Failure(CommandFailureReason.ApiError,
                    string.Format(ResponseMessage.ApiError, "Character List"));
            }

            var characterList = charResponse.Data.FirstOrDefault()?.AvatarList ?? [];
            _ = m_CharacterCache.UpsertCharacters(Game.HonkaiStarRail, characterList.Select(x => x.Name));

            var fileName = GetFileName(JsonSerializer.Serialize(characterList), "jpg", gameUid);
            if (await AttachmentExistsAsync(fileName))
            {
                return CommandResult.Success([
                    new CommandText($"<@{context.UserId}>")
                    , new CommandAttachment(fileName)
                ]);
            }

            var avatarTask = characterList.Select(x => m_ImageUpdaterService
                .UpdateImageAsync(x.ToAvatarImageData(), ImageProcessors.AvatarProcessor));
            var weaponTask =
                characterList.Where(x => x.Equip is not null).Select(x =>
                    m_ImageUpdaterService.UpdateImageAsync(x.Equip!.ToIconImageData(),
                        new ImageProcessorBuilder().Resize(150, 0).Build()));

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
        catch (CommandException e)
        {
            Logger.LogError(e, LogMessage.UnknownError, "CharList", context.UserId, e.Message);
            return CommandResult.Failure(CommandFailureReason.BotError,
                string.Format(ResponseMessage.CardGenError, "Character List"));
        }
        catch (Exception e)
        {
            Logger.LogError(e, LogMessage.UnknownError, "CharList", context.UserId, e.Message);
            return CommandResult.Failure(CommandFailureReason.Unknown, ResponseMessage.UnknownError);
        }
    }
}
