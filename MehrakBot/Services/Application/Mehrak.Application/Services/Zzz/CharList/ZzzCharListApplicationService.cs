using System.Text.Json;
using Mehrak.Application.Builders;
using Mehrak.Application.Services.Abstractions;
using Mehrak.Application.Services.Common;
using Mehrak.Application.Services.Common.Types;
using Mehrak.Application.Utility;
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
using Mehrak.Infrastructure.Context;

namespace Mehrak.Application.Services.Zzz.CharList;

public class ZzzCharListApplicationService : BaseAttachmentApplicationService
{
    private readonly ICardService<(IEnumerable<ZzzBasicAvatarData>, IEnumerable<ZzzBuddyData>)> m_CardService;
    private readonly IImageUpdaterService m_ImageUpdaterService;
    private readonly ICharacterApiService<ZzzBasicAvatarData, ZzzFullAvatarData, CharacterApiContext> m_CharacterApi;
    private readonly IApiService<IEnumerable<ZzzBuddyData>, BaseHoYoApiContext> m_BuddyApi;
    private readonly ICharacterCacheService m_CharacterCacheService;


    protected override string CommandName => "CharList";
    protected override string CardName => "Character List";
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

        var charResponse = await m_CharacterApi.GetAllCharactersAsync(
            new CharacterApiContext(context.UserId, context.LtUid, context.LToken, gameUid, region), cancellationToken);

        if (!charResponse.IsSuccess)
        {
            if (charResponse.StatusCode == StatusCode.Cancelled)
                throw new OperationCanceledException(charResponse.ErrorMessage ?? "Cancelled");
            if (charResponse.StatusCode == StatusCode.Timeout)
                return CommandResult.Failure(CommandFailureReason.Timeout, ResponseMessage.TimeoutError);
            Logger.LogError(LogMessage.ApiError, "Character List", context.UserId, gameUid, charResponse);
            return CommandResult.Failure(CommandFailureReason.ApiError,
                string.Format(ResponseMessage.ApiError, "Character List"));
        }

        var characters = charResponse.Data;
        _ = m_CharacterCacheService.UpsertCharacters(Game.ZenlessZoneZero,
            characters.Select(x => new CharacterUpsertEntry(x.Name, x.Id)));

        var buddyResponse = await m_BuddyApi.GetAsync(
            new BaseHoYoApiContext(context.UserId, context.LtUid, context.LToken, gameUid, region), cancellationToken);

        if (!buddyResponse.IsSuccess)
        {
            if (buddyResponse.StatusCode == StatusCode.Cancelled)
                throw new OperationCanceledException(buddyResponse.ErrorMessage ?? "Cancelled");
            if (buddyResponse.StatusCode == StatusCode.Timeout)
                return CommandResult.Failure(CommandFailureReason.Timeout, ResponseMessage.TimeoutError);
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
            m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(), ImageProcessors.AvatarProcessor, cancellationToken));
        var buddyTask = buddies.Select(x =>
            m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(), new ImageProcessorBuilder().Resize(300, 0).Build(), cancellationToken));

        var completed = await Task.WhenAll(avatarTask.Concat(buddyTask));

        if (completed.Any(x => !x))
        {
            Logger.LogError(LogMessage.ImageUpdateError, "CharList", context.UserId,
                $"{JsonSerializer.Serialize(characters)}\n{JsonSerializer.Serialize(buddies)}");
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
}
