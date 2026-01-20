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
using Mehrak.GameApi.Zzz.Types;
using Mehrak.Infrastructure.Context;

namespace Mehrak.Application.Services.Zzz.CharList;

public class ZzzCharListApplicationService : BaseAttachmentApplicationService
{
    private readonly ICardService<(IEnumerable<ZzzBasicAvatarData>, IEnumerable<ZzzBuddyData>)> m_CardService;
    private readonly IImageUpdaterService m_ImageUpdaterService;
    private readonly ICharacterApiService<ZzzBasicAvatarData, ZzzFullAvatarData, CharacterApiContext> m_CharacterApi;
    private readonly IApiService<IEnumerable<ZzzBuddyData>, BaseHoYoApiContext> m_BuddyApi;
    private readonly ICharacterCacheService m_CharacterCacheService;

    public ZzzCharListApplicationService(
        ICardService<(IEnumerable<ZzzBasicAvatarData>, IEnumerable<ZzzBuddyData>)> cardService,
        IImageUpdaterService imageUpdaterService,
        ICharacterApiService<ZzzBasicAvatarData, ZzzFullAvatarData, CharacterApiContext> characterApi,
        IApiService<IEnumerable<ZzzBuddyData>, BaseHoYoApiContext> buddyApi,
        ICharacterCacheService characterCacheService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        UserDbContext userContext,
        IAttachmentStorageService attachmentStorageService,
        ILogger<ZzzCharListApplicationService> logger)
        : base(gameRoleApi, userContext, attachmentStorageService, logger)
    {
        m_CardService = cardService;
        m_ImageUpdaterService = imageUpdaterService;
        m_CharacterApi = characterApi;
        m_BuddyApi = buddyApi;
        m_CharacterCacheService = characterCacheService;
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

            var charResponse = await m_CharacterApi.GetAllCharactersAsync(
                new CharacterApiContext(context.UserId, context.LtUid, context.LToken, gameUid, region));

            if (!charResponse.IsSuccess)
            {
                Logger.LogError(LogMessage.ApiError, "Character List", context.UserId, gameUid, charResponse);
                return CommandResult.Failure(CommandFailureReason.ApiError,
                    string.Format(ResponseMessage.ApiError, "Character List"));
            }

            var characters = charResponse.Data;
            _ = m_CharacterCacheService.UpsertCharacters(Game.ZenlessZoneZero, characters.Select(x => x.Name));

            var buddyResponse = await m_BuddyApi.GetAsync(
                new BaseHoYoApiContext(context.UserId, context.LtUid, context.LToken, gameUid, region));

            if (!buddyResponse.IsSuccess)
            {
                Logger.LogError(LogMessage.ApiError, "Bangboo List", context.UserId, gameUid, buddyResponse);
                return CommandResult.Failure(CommandFailureReason.ApiError,
                    string.Format(ResponseMessage.ApiError, "Bangboo List"));
            }

            var buddies = buddyResponse.Data;

            var filename = GetFileName($"{JsonSerializer.Serialize(characters)}_{JsonSerializer.Serialize(buddies)}",
                "jpg", profile.GameUid);
            if (await AttachmentExistsAsync(filename))
            {
                return CommandResult.Success(
                [
                    new CommandText($"<@{context.UserId}>"),
                    new CommandAttachment(filename)
                ]);
            }

            var avatarTask = characters.Select(x =>
                m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(), ImageProcessors.AvatarProcessor));
            var buddyTask = buddies.Select(x =>
                m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(), new ImageProcessorBuilder().Resize(300, 0).Build()));

            var completed = await Task.WhenAll(avatarTask.Concat(buddyTask));

            if (completed.Any(x => !x))
            {
                Logger.LogError(LogMessage.ImageUpdateError, "CharList", context.UserId,
                    "One or more images failed to update");
                return CommandResult.Failure(CommandFailureReason.BotError, ResponseMessage.ImageUpdateError);
            }

            var cardContext = new
                BaseCardGenerationContext<(IEnumerable<ZzzBasicAvatarData>, IEnumerable<ZzzBuddyData>)>(
                    context.UserId,
                    (characters, buddies), profile);
            cardContext.SetParameter("server", server);

            using var card = await m_CardService.GetCardAsync(cardContext);
            if (!await StoreAttachmentAsync(context.UserId, filename, card))
            {
                Logger.LogError(LogMessage.AttachmentStoreError, filename, context.UserId);
                return CommandResult.Failure(CommandFailureReason.BotError,
                    ResponseMessage.AttachmentStoreError);
            }

            return CommandResult.Success([
                new CommandText($"<@{context.UserId}>"), new CommandAttachment(filename)
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
