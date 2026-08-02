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
using Mehrak.Domain.Image.Abstractions;
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
    private readonly IPortraitMatcher m_PortraitMatcher;
    private readonly IImageFetcher m_ImageFetcher;

    private static readonly TimeSpan OutfitMatchingTimeout = TimeSpan.FromSeconds(60);


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
        IPortraitMatcher portraitMatcher,
        IImageFetcher imageFetcher,
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
        m_PortraitMatcher = portraitMatcher;
        m_ImageFetcher = imageFetcher;
    }

    protected override async Task<CommandResult> ExecuteCommandAsync(IApplicationContext context, CancellationToken cancellationToken = default)
    {
        var characterName = context.GetParameter("character")!;

        var server = Enum.Parse<Server>(context.GetParameter("server")!);
        var region = server.ToRegion();

        var (profile, charResponse) = await FetchProfileAndPrimaryAsync(
            context.UserId, context.LtUid, context.LToken, Game.ZenlessZoneZero, region,
            uid => m_CharacterApi.GetAllCharactersAsync(
                new CharacterApiContext(context.UserId, context.LtUid, context.LToken, uid, region), cancellationToken),
            cancellationToken);

        var gameUid = profile.GameUid;

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

        Task<Result<JsonArray>>? galleryTask = null;

        List<Task<bool>> tasks = [];
        Task<bool>? basePortraitUpdateTask = null;
        Task<bool>? outfitPortraitUpdateTask = null;

        var basePortraitName = string.Format(FileNameFormat.Zzz.PortraitName, charInfo.Id);
        var portraitName = charInfo.ToImageName();
        var isOutfitPortrait = !portraitName.Equals(basePortraitName, StringComparison.OrdinalIgnoreCase);
        var basePortraitMissing = !await m_ImageRepository.FileExistsAsync(basePortraitName, cancellationToken);
        var outfitPortraitMissing = isOutfitPortrait &&
            !await m_ImageRepository.FileExistsAsync(portraitName, cancellationToken);
        var hasExistingPortrait = !basePortraitMissing || (isOutfitPortrait && !outfitPortraitMissing);

        if (basePortraitMissing || outfitPortraitMissing)
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
                    galleryTask = GetCharacterGalleryAsync(context, entryPage, cancellationToken);
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
            if (galleryTask != null)
            {
                var galleryResult = await galleryTask;
                if (!galleryResult.IsSuccess)
                {
                    if (galleryResult.StatusCode == StatusCode.Cancelled)
                        throw new OperationCanceledException(galleryResult.ErrorMessage ?? "Cancelled");
                    if (!hasExistingPortrait)
                    {
                        if (galleryResult.StatusCode == StatusCode.Timeout)
                            return CommandResult.Failure(CommandFailureReason.Timeout, ResponseMessage.TimeoutError);
                        Logger.LogError("Failed to fetch Character {Character} image from wiki", charInfo.Name);
                        return CommandResult.Failure(CommandFailureReason.ApiError,
                            string.Format(ResponseMessage.ApiError, "Character Image"));
                    }

                    Logger.LogWarning("Failed to fetch Character {Character} image from wiki; using existing portrait",
                        charInfo.Name);
                }
                else
                {
                    var galleryList = galleryResult.Data;
                    if (basePortraitMissing)
                    {
                        var baseUrl = GetGalleryImageUrl(galleryList[0]);
                        if (!string.IsNullOrEmpty(baseUrl))
                        {
                            basePortraitUpdateTask = UpdateOptionalPortraitAsync(
                                new ImageData(basePortraitName, baseUrl), cancellationToken);
                            tasks.Add(basePortraitUpdateTask);
                        }
                    }

                    if (outfitPortraitMissing)
                    {
                        var outfitUrl = await FindOutfitPortraitUrlAsync(galleryList, charInfo, cancellationToken);
                        if (!string.IsNullOrEmpty(outfitUrl))
                        {
                            outfitPortraitUpdateTask = UpdateOptionalPortraitAsync(
                                new ImageData(portraitName, outfitUrl), cancellationToken);
                            tasks.Add(outfitPortraitUpdateTask);
                        }
                    }
                }
            }
        }
        finally
        {
            await Task.WhenAll(tasks);
        }

        var requiredImageUpdateFailed = tasks
            .Where(x => x != basePortraitUpdateTask && x != outfitPortraitUpdateTask)
            .Any(x => !x.Result);

        if (requiredImageUpdateFailed)
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

    private async Task<bool> UpdateOptionalPortraitAsync(IImageData data,
        CancellationToken cancellationToken)
    {
        try
        {
            return await m_ImageUpdaterService.UpdateImageAsync(data, ImageProcessors.None, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to update optional portrait {PortraitName}", data.Name);
            return false;
        }
    }

    private async Task<Result<JsonArray>> GetCharacterGalleryAsync(IApplicationContext context,
        string entryPage, CancellationToken cancellationToken = default)
    {
        var cnResult = await m_WikiApi.GetAsync(new WikiApiContext(context.UserId, Game.ZenlessZoneZero, entryPage, WikiLocales.CN), cancellationToken);
        if (cnResult.IsSuccess)
        {
            var cnList = ParseZzzCharacterGallery(cnResult.Data);
            if (HasValidBasePortrait(cnList))
                return Result<JsonArray>.Success(cnList!);
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
            return ParseZzzCharacterGallery(result.Data);
        }).ToList();

        var results = await Task.WhenAll(tasks);
        var list = results.FirstOrDefault(HasValidBasePortrait);

        if (list == null)
        {
            return Result<JsonArray>.Failure(bestStatus, "Character image not found");
        }

        return Result<JsonArray>.Success(list);
    }

    private static JsonArray? ParseZzzCharacterGallery(JsonNode data)
    {
        var jsonStr = data["data"]?["page"]?["modules"]?.AsArray()
            .SelectMany(x => x?["components"]?.AsArray() ?? [])
            .FirstOrDefault(x => x?["component_id"]?.GetValue<string>() == "gallery_character")
            ?["data"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(jsonStr))
            return null;

        return JsonNode.Parse(jsonStr)?["list"]?.AsArray();
    }

    private static string? GetGalleryImageUrl(JsonNode? entry)
    {
        try
        {
            var url = entry?["img"]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(url) ? null : url;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static bool HasValidBasePortrait(JsonArray? gallery)
    {
        return gallery is { Count: > 0 } && GetGalleryImageUrl(gallery[0]) != null;
    }

    /// <summary>
    /// Finds which gallery entry matches the character's currently equipped outfit by
    /// matching its square face crop (<see cref="ZzzAvatarData.RoleSquareUrl"/>) against
    /// every png gallery entry after the base portrait. Returns the matching image URL or
    /// <see langword="null"/> when no candidate matches, warranting a manual check.
    /// </summary>
    private async Task<string?> FindOutfitPortraitUrlAsync(JsonArray galleryList, ZzzAvatarData charInfo,
        CancellationToken cancellationToken = default)
    {
        var candidates = galleryList.Skip(1)
            .Select(GetGalleryImageUrl)
            .Where(x => !string.IsNullOrEmpty(x) &&
                x.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            .Cast<string>()
            .ToList();

        if (candidates.Count == 0)
        {
            Logger.LogWarning("Cannot find outfit for {CharacterId} when outfit exists, {RoleSquareUrl}",
                charInfo.Id, charInfo.RoleSquareUrl);
            return null;
        }

        using var matchingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        matchingCts.CancelAfter(OutfitMatchingTimeout);

        try
        {
            var referenceBytes = await m_ImageFetcher.FetchBytesAsync(charInfo.RoleSquareUrl, matchingCts.Token);
            if (referenceBytes == null)
            {
                Logger.LogWarning("Failed to fetch reference image {RoleSquareUrl} for outfit matching", charInfo.RoleSquareUrl);
                return null;
            }

            foreach (var candidate in candidates)
            {
                var candidateBytes = await m_ImageFetcher.FetchBytesAsync(candidate, matchingCts.Token);
                if (candidateBytes == null)
                    continue;

                var (isMatch, _) = await m_PortraitMatcher.MatchAsync(referenceBytes, candidateBytes, matchingCts.Token);
                if (isMatch)
                    return candidate;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Logger.LogWarning("Outfit matching timed out for {CharacterId}", charInfo.Id);
            return null;
        }

        Logger.LogWarning("Cannot find outfit for {CharacterId} when outfit exists, {RoleSquareUrl}",
            charInfo.Id, charInfo.RoleSquareUrl);
        return null;
    }
}
