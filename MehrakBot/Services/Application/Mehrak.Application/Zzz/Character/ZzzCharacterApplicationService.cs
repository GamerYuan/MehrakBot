#region

using System.Text.Json;
using System.Text.Json.Nodes;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Builders;
using Mehrak.Application.Shared.Services;
using Mehrak.Application.Shared.Services.Types;
using Mehrak.Application.Shared.Utility;
using Mehrak.Domain.Card;
using Mehrak.Domain.Character;
using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Image;
using Mehrak.Domain.Image.Models;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.Domain.User.Models;
using Mehrak.GameApi.GameRole;
using Mehrak.GameApi.Shared.Types;
using Mehrak.GameApi.Wiki;
using Mehrak.GameApi.Zzz;
using Mehrak.GameApi.Zzz.Types;
using Mehrak.Infrastructure.User;

#endregion

namespace Mehrak.Application.Zzz.Character;

internal class ZzzCharacterApplicationService : BaseAttachmentApplicationService
{
    private readonly ICardService<ZzzFullAvatarData> m_CardService;
    private readonly IImageUpdaterService m_ImageUpdaterService;
    private readonly IImageRepository m_ImageRepository;
    private readonly ICharacterApiService<ZzzBasicAvatarData, ZzzFullAvatarData, CharacterApiContext> m_CharacterApi;
    private readonly ICharacterCacheService m_CharacterCacheService;
    private readonly IAliasService m_AliasService;
    private readonly IApiService<JsonNode, WikiApiContext> m_WikiApi;
    private readonly IApplicationMetrics m_MetricsService;
    private readonly ICharacterPortraitConfigService m_PortraitConfigService;
    private readonly IUserPortraitService m_UserPortraitService;
    private readonly IApiService<ZzzCharacterEntryPageList, ZzzCharacterEntryPageApiContext> m_CharacterEntryPageService;


    protected override string CommandName => "ZZZ Character";
    protected override string CardName => "Character";
    public ZzzCharacterApplicationService(
        ICardService<ZzzFullAvatarData> cardService,
        IImageUpdaterService imageUpdaterService,
        IImageRepository imageRepository,
        ICharacterApiService<ZzzBasicAvatarData, ZzzFullAvatarData, CharacterApiContext> characterApi,
        ICharacterCacheService characterCacheService,
        IAliasService aliasService,
        IApiService<JsonNode, WikiApiContext> wikiApi,
        IApplicationMetrics metricsService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        UserDbContext userContext,
        IAttachmentStorageService attachmentStorageService,
        ICharacterPortraitConfigService portraitConfigService,
        IUserPortraitService userPortraitService,
        IApiService<ZzzCharacterEntryPageList, ZzzCharacterEntryPageApiContext> characterEntryPageService,
        ILogger<ZzzCharacterApplicationService> logger)
        : base(gameRoleApi, userContext, attachmentStorageService, logger)
    {
        m_CardService = cardService;
        m_ImageUpdaterService = imageUpdaterService;
        m_ImageRepository = imageRepository;
        m_CharacterApi = characterApi;
        m_CharacterCacheService = characterCacheService;
        m_AliasService = aliasService;
        m_WikiApi = wikiApi;
        m_MetricsService = metricsService;
        m_PortraitConfigService = portraitConfigService;
        m_UserPortraitService = userPortraitService;
        m_CharacterEntryPageService = characterEntryPageService;
    }

    protected override async Task<CommandResult> ExecuteCommandAsync(IApplicationContext context, CancellationToken cancellationToken = default)
    {
        var characterName = context.GetParameter("character")!;

        var server = Enum.Parse<Server>(context.GetParameter("server")!);
        var region = server.ToRegion();

        var cachedGameUid = await GetCachedGameUidAsync(context.UserId, context.LtUid, Game.ZenlessZoneZero, region, cancellationToken);
        var profileTask = FetchGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.ZenlessZoneZero,
            region, cancellationToken);

        Task<Result<IEnumerable<ZzzBasicAvatarData>>>? primaryTask = null;
        if (cachedGameUid != null)
        {
            primaryTask = m_CharacterApi.GetAllCharactersAsync(
                new CharacterApiContext(context.UserId, context.LtUid, context.LToken, cachedGameUid, region), cancellationToken);
        }

        var profileResult = await profileTask;
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

        if (cachedGameUid == null)
        {
            await SaveGameUidAsync(context.UserId, context.LtUid, Game.ZenlessZoneZero, region, profile.GameUid, profile.Level, cancellationToken);
            primaryTask = m_CharacterApi.GetAllCharactersAsync(
                new CharacterApiContext(context.UserId, context.LtUid, context.LToken, profile.GameUid, region), cancellationToken);
        }

        var charResponse = await primaryTask!;

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

        var character = characters.FirstOrDefault(x =>
            x.Name.Equals(characterName, StringComparison.OrdinalIgnoreCase) ||
            x.FullName.Equals(characterName, StringComparison.OrdinalIgnoreCase));

        if (character == null)
        {
            m_AliasService.GetAliases(Game.ZenlessZoneZero).TryGetValue(characterName, out var name);

            if (name == null ||
                (character =
                    characters.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) ==
                null)
            {
                Logger.LogInformation(LogMessage.CharNotFoundInfo, characterName, context.UserId, gameUid);
                return CommandResult.Success(
                    [new CommandText(string.Format(ResponseMessage.CharacterNotFound, characterName))],
                    isEphemeral: true);
            }
        }

        var response = await
            m_CharacterApi.GetCharacterDetailAsync(new CharacterApiContext(context.UserId, context.LtUid,
                context.LToken, gameUid, region, character.Id!), cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.StatusCode == StatusCode.Cancelled)
                throw new OperationCanceledException(response.ErrorMessage ?? "Cancelled");
            if (response.StatusCode == StatusCode.Timeout)
                return CommandResult.Failure(CommandFailureReason.Timeout, ResponseMessage.TimeoutError);
            Logger.LogError(LogMessage.ApiError, "Character", context.UserId, gameUid, response);
            return CommandResult.Failure(CommandFailureReason.ApiError,
                string.Format(ResponseMessage.ApiError, "Character data"));
        }

        var characterData = response.Data;
        var charInfo = characterData.AvatarList[0];

        var activePortrait = await PortraitResolutionHelper.GetActivePortraitAsync(
            m_UserPortraitService, context.UserId, Game.ZenlessZoneZero, charInfo.Name, cancellationToken);

        var extraData = activePortrait != null
            ? $"{activePortrait.Key}_{JsonSerializer.Serialize(activePortrait.Config)}"
            : null;
        var fileName = GetFileName(JsonSerializer.Serialize(characterData), "jpg", gameUid, extraData);
        if (await AttachmentExistsAsync(fileName))
        {
            m_MetricsService.TrackCharacterSelection(nameof(Game.ZenlessZoneZero), charInfo.Name.ToLowerInvariant());
            return CommandResult.Success([
                new CommandText($"<@{context.UserId}>", CommandText.TextType.Header3),
                    new CommandAttachment(fileName)
            ]);
        }

        Task<Result<string>>? charImageUrlTask = null;

        List<Task<bool>> tasks = [];

        if (!await m_ImageRepository.FileExistsAsync(charInfo.ToImageName()))
        {
            if (!characterData.AvatarWiki.TryGetValue(charInfo.Id.ToString(), out var avatarWikiUrl))
            {
                Logger.LogWarning("Character '{Character}' (Id={Id}) not found in wiki data",
                    charInfo.FullName, charInfo.Id);
            }
            else
            {
                var entryPage = string.Empty;

                if (avatarWikiUrl.Contains("/aggregate/"))
                {
                    var entryPageResult = await m_CharacterEntryPageService.GetAsync(
                        new ZzzCharacterEntryPageApiContext(context.UserId), cancellationToken);
                    if (entryPageResult.IsSuccess)
                    {
                        var entry = entryPageResult.Data.List.FirstOrDefault(x =>
                            x.Name.Equals(charInfo.FullName, StringComparison.OrdinalIgnoreCase) ||
                            x.Name.Equals(charInfo.Name, StringComparison.OrdinalIgnoreCase));
                        if (entry != null)
                            entryPage = entry.EntryPageId;
                        else
                            Logger.LogWarning("Character '{Character}' not found in ZZZ entry page list", charInfo.FullName);
                    }
                    else
                    {
                        Logger.LogWarning("Failed to get ZZZ entry page list: {Message}", entryPageResult.ErrorMessage);
                    }
                }
                else
                {
                    entryPage = avatarWikiUrl.Split('/')[^1];
                }

                if (!string.IsNullOrEmpty(entryPage))
                    charImageUrlTask = GetCharacterImageUrlAsync(context, gameUid, charInfo, entryPage, cancellationToken);
            }
        }

        if (charInfo.Weapon != null)
            tasks.Add(m_ImageUpdaterService.UpdateImageAsync(charInfo.Weapon.ToImageData(),
                new ImageProcessorBuilder().Resize(150, 0).Build(), cancellationToken));

        tasks.AddRange(charInfo.Equip.DistinctBy(x => x.EquipSuit)
            .Select(x =>
                m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(),
                    new ImageProcessorBuilder().Resize(140, 0).Build(), cancellationToken)));

        try
        {
            if (charImageUrlTask != null)
            {
                var charImage = await charImageUrlTask;
                if (!charImage.IsSuccess)
                {
                    if (charImage.StatusCode == StatusCode.Cancelled)
                        throw new OperationCanceledException(charImage.ErrorMessage ?? "Cancelled");
                    if (charImage.StatusCode == StatusCode.Timeout)
                        return CommandResult.Failure(CommandFailureReason.Timeout, ResponseMessage.TimeoutError);
                    Logger.LogError("Failed to fetch Character {Character} image from wiki", charInfo.Name);
                    return CommandResult.Failure(CommandFailureReason.ApiError,
                        string.Format(ResponseMessage.ApiError, "Character Image"));
                }

                var url = charImage.Data;
                tasks.Add(m_ImageUpdaterService.UpdateImageAsync(new ImageData(charInfo.ToImageName(),
                    url), ImageProcessors.None, cancellationToken));
            }
        }
        finally
        {
            await Task.WhenAll(tasks);
        }

        var completed = tasks.Select(x => x.Result).ToArray();

        if (completed.Any(x => !x))
        {
            Logger.LogError(LogMessage.ImageUpdateError, "Character", context.UserId,
                JsonSerializer.Serialize(charInfo));
            return CommandResult.Failure(CommandFailureReason.ApiError, ResponseMessage.ImageUpdateError);
        }

        var cardContext = new BaseCardGenerationContext<ZzzFullAvatarData>(context.UserId, characterData, profile);
        cardContext.SetParameter("server", server);

        var resolution = activePortrait != null
            ? await PortraitResolutionHelper.ResolveActivePortraitAsync(
                m_UserPortraitService, context.UserId, activePortrait,
                () => m_PortraitConfigService.GetConfigAsync(Game.ZenlessZoneZero, charInfo.Id), cancellationToken)
            : new PortraitResolution(null,
                await m_PortraitConfigService.GetConfigAsync(Game.ZenlessZoneZero, charInfo.Id));
        cardContext.PortraitImageStream = resolution.ImageStream;
        cardContext.PortraitConfig = resolution.Config;

        try
        {
            await using var card = await m_CardService.GetCardAsync(cardContext);
            if (!await StoreAttachmentAsync(context.UserId, fileName, card))
            {
                Logger.LogError(LogMessage.AttachmentStoreError, fileName, context.UserId);
                return CommandResult.Failure(CommandFailureReason.BotError, ResponseMessage.AttachmentStoreError);
            }
        }
        finally
        {
            if (resolution.ImageStream != null)
                await resolution.ImageStream.DisposeAsync();
        }

        m_MetricsService.TrackCharacterSelection(nameof(Game.ZenlessZoneZero), charInfo.Name.ToLowerInvariant());

        return CommandResult.Success([
            new CommandText($"<@{context.UserId}>", CommandText.TextType.Header3), new CommandAttachment(fileName)
        ]);
    }

    private async Task<Result<string>> GetCharacterImageUrlAsync(IApplicationContext context, string gameUid,
        ZzzAvatarData charInfo, string entryPage, CancellationToken cancellationToken = default)
    {
        var cnResult = await m_WikiApi.GetAsync(new WikiApiContext(context.UserId, Game.ZenlessZoneZero, entryPage, WikiLocales.CN), cancellationToken);
        if (cnResult.IsSuccess)
        {
            var cnUrl = ParseZzzCharacterImageUrl(cnResult.Data);
            if (!string.IsNullOrEmpty(cnUrl))
                return Result<string>.Success(cnUrl);
        }

        var otherLocales = Enum.GetValues<WikiLocales>().Where(x => x != WikiLocales.CN);
        var bestStatus = StatusCode.ExternalServerError;
        var tasks = otherLocales.Select(async locale =>
        {
            var result = await m_WikiApi.GetAsync(new WikiApiContext(context.UserId, Game.ZenlessZoneZero, entryPage, locale), cancellationToken);
            if (!result.IsSuccess)
            {
                if (result.StatusCode == StatusCode.Cancelled) bestStatus = StatusCode.Cancelled;
                else if (result.StatusCode == StatusCode.Timeout && bestStatus != StatusCode.Cancelled) bestStatus = StatusCode.Timeout;
                return null;
            }
            return ParseZzzCharacterImageUrl(result.Data);
        }).ToList();

        var results = await Task.WhenAll(tasks);
        var url = results.FirstOrDefault(x => !string.IsNullOrEmpty(x));

        if (string.IsNullOrEmpty(url))
        {
            return Result<string>.Failure(bestStatus, "Character image not found");
        }

        return Result<string>.Success(url);
    }

    private static string? ParseZzzCharacterImageUrl(JsonNode data)
    {
        var jsonStr = data["data"]?["page"]?["modules"]?.AsArray()
            .SelectMany(x => x?["components"]?.AsArray() ?? [])
            .FirstOrDefault(x => x?["component_id"]?.GetValue<string>() == "gallery_character")
            ?["data"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(jsonStr))
            return null;

        return JsonNode.Parse(jsonStr)
            ?["list"]?.AsArray().FirstOrDefault()?["img"]?.GetValue<string>();
    }
}
