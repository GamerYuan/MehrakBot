#region

using System.Text.Json;
using System.Text.Json.Nodes;
using Mehrak.Application.Genshin.Extensions;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Builders;
using Mehrak.Application.Shared.Models;
using Mehrak.Application.Shared.Renderers.Extensions;
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
using Mehrak.Domain.Shared.Utility;
using Mehrak.Domain.User.Models;
using Mehrak.GameApi.GameRole;
using Mehrak.GameApi.Genshin.Types;
using Mehrak.GameApi.Wiki;
using Mehrak.Infrastructure.User;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Genshin.Character;

internal class GenshinCharacterApplicationService : BaseAttachmentApplicationService
{
    private const int MaxRequestCount = 4;

    private readonly ICardService<GenshinCharacterInformation> m_CardService;
    private readonly ICharacterCacheService m_CharacterCacheService;
    private readonly IAliasService m_AliasService;

    private readonly ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, GenshinCharacterApiContext>
        m_CharacterApi;

    private readonly IApiService<JsonNode, WikiApiContext> m_WikiApi;
    private readonly IImageRepository m_ImageRepository;
    private readonly IImageUpdaterService m_ImageUpdaterService;
    private readonly IApplicationMetrics m_MetricsService;
    private readonly ICharacterStatService m_CharacterStatService;
    private readonly ICharacterPortraitConfigService m_PortraitConfigService;
    private readonly IUserPortraitService m_UserPortraitService;
    private readonly IMultiImageProcessor m_WeaponImageProcessor;
    private readonly IOptions<CommandDispatcherConfig> m_DispatcherConfig;


    protected override string CommandName => "Genshin Character";
    protected override string CardName => "Character";
    public GenshinCharacterApplicationService(
        ICardService<GenshinCharacterInformation> cardService,
        ICharacterCacheService characterCacheService,
        IAliasService aliasService,
        ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, GenshinCharacterApiContext> characterApi,
        IApiService<JsonNode, WikiApiContext> wikiApi,
        IImageRepository imageRepository,
        IImageUpdaterService imageUpdaterService,
        IApplicationMetrics metricsService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        UserDbContext userContext,
        ICharacterStatService characterStatService,
        IAttachmentStorageService attachmentStorage,
        ICharacterPortraitConfigService portraitConfigService,
        IUserPortraitService userPortraitService,
        [FromKeyedServices(Mehrak.Domain.Shared.Common.CommandName.ImageProcessor.Weapon)] IMultiImageProcessor weaponImageProcessor,
        IOptions<CommandDispatcherConfig> dispatcherConfig,
        ILogger<GenshinCharacterApplicationService> logger)
        : base(gameRoleApi, userContext, attachmentStorage, logger)
    {
        m_CardService = cardService;
        m_CharacterCacheService = characterCacheService;
        m_AliasService = aliasService;
        m_CharacterApi = characterApi;
        m_WikiApi = wikiApi;
        m_ImageRepository = imageRepository;
        m_ImageUpdaterService = imageUpdaterService;
        m_MetricsService = metricsService;
        m_CharacterStatService = characterStatService;
        m_PortraitConfigService = portraitConfigService;
        m_UserPortraitService = userPortraitService;
        m_WeaponImageProcessor = weaponImageProcessor;
        m_DispatcherConfig = dispatcherConfig;
    }

    protected override async Task<CommandResult> ExecuteCommandAsync(IApplicationContext context, CancellationToken cancellationToken = default)
    {
        var server = Enum.Parse<Server>(context.GetParameter("server")!);
        var region = server.ToRegion();
        var input = context.GetParameter("character")!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (input.Length > MaxRequestCount)
        {
            return CommandResult.Success(
                [new CommandText("Exceeded the maximum number of characters per request! (Max 4)")],
                isEphemeral: true);
        }

        var profileResult =
            await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.Genshin, region, cancellationToken);
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

        _ = UpdateGameUidAsync(context.UserId, context.LtUid, Game.Genshin, profile.GameUid, server.ToString(), cancellationToken);

        var gameUid = profile.GameUid;

        var charListResponse = await
            m_CharacterApi.GetAllCharactersAsync(new GenshinCharacterApiContext(context.UserId, context.LtUid,
                context.LToken, gameUid, region), cancellationToken);
        if (!charListResponse.IsSuccess)
        {
            if (charListResponse.StatusCode == StatusCode.Cancelled)
                throw new OperationCanceledException(charListResponse.ErrorMessage ?? "Cancelled");
            if (charListResponse.StatusCode == StatusCode.Timeout)
                return CommandResult.Failure(CommandFailureReason.Timeout, ResponseMessage.TimeoutError);
            Logger.LogError(LogMessage.ApiError, "Character List", context.UserId, profile.GameUid,
                charListResponse);
            return CommandResult.Failure(CommandFailureReason.ApiError,
                string.Format(ResponseMessage.ApiError, "Character List"));
        }

        var characters = charListResponse.Data;
        _ = m_CharacterCacheService.UpsertCharacters(Game.Genshin,
            characters.Select(x => new CharacterUpsertEntry(x.Name, x.Id)));

        var names = characters.ToDictionary(x => x.Name, x => x, StringComparer.OrdinalIgnoreCase);
        HashSet<int> validCharacters = [];
        List<string> failureMessages = [];

        foreach (var c in input)
        {
            var character = names.GetValueOrDefault(c);

            if (character == null)
            {
                m_AliasService.GetAliases(Game.Genshin).TryGetValue(c, out var name);

                if (name == null ||
                    (character = names.GetValueOrDefault(name)) == null)
                {
                    Logger.LogInformation(LogMessage.CharNotFoundInfo, c, context.UserId, gameUid);
                    failureMessages.Add(string.Format(ResponseMessage.CharacterNotFound, c));
                }
            }

            if (character != null)
            {
                validCharacters.Add(character.Id!.Value);
            }
        }

        if (validCharacters.Count == 0)
        {
            return CommandResult.Success(
                [new CommandText(string.Join('\n', failureMessages))],
                isEphemeral: true);
        }

        var characterInfo = await m_CharacterApi.GetCharacterDetailAsync(
            new GenshinCharacterApiContext(context.UserId, context.LtUid, context.LToken, gameUid, region, validCharacters), cancellationToken);

        if (!characterInfo.IsSuccess)
        {
            if (characterInfo.StatusCode == StatusCode.Cancelled)
                throw new OperationCanceledException(characterInfo.ErrorMessage ?? "Cancelled");
            if (characterInfo.StatusCode == StatusCode.Timeout)
                return CommandResult.Failure(CommandFailureReason.Timeout, ResponseMessage.TimeoutError);
            Logger.LogError(LogMessage.ApiError, "Character Detail", context.UserId, profile.GameUid,
                characterInfo);
            return CommandResult.Failure(CommandFailureReason.ApiError,
                string.Format(ResponseMessage.ApiError, "Character data"));
        }

        List<string> attachments = [];

        var (results, timedOut) = await CharacterBatchProcessor.ProcessAsync(
            characterInfo.Data.List,
            (charData, ct) => ProcessCharacterAsync(context, server, profile, charData,
                characterInfo.Data.AvatarWiki, characterInfo.Data.WeaponWiki, ct),
            m_DispatcherConfig.Value.MaxCharacterParallelism,
            cancellationToken);

        if (timedOut)
            return CommandResult.Failure(CommandFailureReason.Timeout, ResponseMessage.TimeoutError);

        for (var i = 0; i < results.Count; i++)
        {
            var result = results[i];
            if (result.IsSuccess)
            {
                attachments.Add(result.Data);
            }
            else
            {
                failureMessages.Add($"{characterInfo.Data.List[i].Base.Name}: {result.ErrorMessage}");
            }
        }

        if (attachments.Count == 0)
        {
            return CommandResult.Failure(CommandFailureReason.ApiError,
                string.Join('\n', failureMessages));
        }

        List<ICommandResultComponent> components = [];
        components.Add(new CommandText($"<@{context.UserId}>"));
        components.AddRange(attachments.Select(x => new CommandAttachment(x)));

        return CommandResult.Success(components,
            ephemeralMessage: string.Join('\n', failureMessages));
    }

    private async Task<Result<string>> ProcessCharacterAsync(
        IApplicationContext context, Server server,
        GameProfileDto profile, GenshinCharacterInformation charData,
        Dictionary<string, string> avatarWiki, Dictionary<string, string> weaponWiki,
        CancellationToken cancellationToken = default)
    {
        var activePortrait = await PortraitResolutionHelper.GetActivePortraitAsync(
            m_UserPortraitService, context.UserId, Game.Genshin, charData.Base.Name, cancellationToken);

        var extraData = activePortrait != null
            ? $"{activePortrait.Key}_{JsonSerializer.Serialize(activePortrait.Config)}"
            : null;
        var filename = GetFileName(JsonSerializer.Serialize(charData), "jpg", profile.GameUid, extraData);
        if (await AttachmentExistsAsync(filename))
        {
            m_MetricsService.TrackCharacterSelection(nameof(Game.Genshin), charData.Base.Name.ToLowerInvariant());
            return Result<string>.Success(filename);
        }

        List<Task<bool>> tasks = [];

        Task<Result<string>>? charImageUrlTask = null;
        Task<Result<string>>? weapImageTask = null;

        if (!await m_ImageRepository.FileExistsAsync(charData.Base.ToImageName()))
        {
            var wikiEntry = avatarWiki[charData.Base.Id.ToString()].Split('/')[^1];
            charImageUrlTask = GetCharacterImageUrlAsync(context, profile, charData, wikiEntry, cancellationToken);
        }

        if (!await m_ImageRepository.FileExistsAsync(charData.Weapon.ToAscendedImageName()))
        {
            var wikiEntry = weaponWiki[charData.Weapon.Id.ToString()!].Split('/')[^1];
            weapImageTask = GetWeaponUrlsAsync(context, profile, charData, wikiEntry, cancellationToken);
        }

        tasks.AddRange(m_ImageUpdaterService.UpdateImageAsync(charData.Weapon.ToImageData(),
            new ImageProcessorBuilder().Resize(200, 0).Build(), cancellationToken));
        tasks.AddRange(charData.Constellations.Select(x => m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(),
            new ImageProcessorBuilder().Resize(90, 0).Build(), cancellationToken)));
        tasks.AddRange(charData.Skills.Select(x => m_ImageUpdaterService.UpdateImageAsync(
            x.ToImageData(charData.Base.Id),
            new ImageProcessorBuilder().Resize(100, 0).Build(), cancellationToken)));
        tasks.AddRange(charData.Relics.Select(x => m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(),
            new ImageProcessorBuilder().Resize(300, 0).AddOperation(ctx => ctx.Pad(300, 300))
                .AddOperation(ctx => ctx.ApplyGradientFade(0.5f, EasingType.InQuint)).Build(), cancellationToken)));

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
                        return Result<string>.Failure(StatusCode.Timeout, ResponseMessage.TimeoutError);
                    Logger.LogError("Failed to fetch Character {Character} image from wiki", charData.Base.Name);
                    return Result<string>.Failure(StatusCode.ExternalServerError,
                        string.Format(ResponseMessage.ApiError, "Character Image"));
                }

                var url = charImage.Data;
                tasks.Add(m_ImageUpdaterService.UpdateImageAsync(new ImageData(charData.Base.ToImageName(), url),
                    ImageProcessors.None, cancellationToken));
            }

            if (weapImageTask != null)
            {
                var weapImage = await weapImageTask;
                if (weapImage.IsSuccess)
                {
                    await m_ImageUpdaterService.UpdateMultiImageAsync(
                        new MultiImageData(charData.Weapon.ToAscendedImageName(),
                            [charData.Weapon.Icon, weapImage.Data]),
                        m_WeaponImageProcessor,
                        cancellationToken
                    );
                }
            }
        }
        finally
        {
            await Task.WhenAll(tasks);
        }

        if (tasks.Any(x => !x.Result))
        {
            Logger.LogError(LogMessage.ImageUpdateError, "Character", context.UserId,
                JsonSerializer.Serialize(charData));
            return Result<string>.Failure(StatusCode.ExternalServerError, ResponseMessage.ImageUpdateError);
        }

        var statTask = m_CharacterStatService.GetCharAscStatAsync(Game.Genshin, charData.Base.Name);
        var portraitTask = activePortrait != null
            ? PortraitResolutionHelper.ResolveActivePortraitAsync(
                m_UserPortraitService, context.UserId, activePortrait,
                () => m_PortraitConfigService.GetConfigAsync(Game.Genshin, charData.Base.Id), cancellationToken)
            : m_PortraitConfigService.GetConfigAsync(Game.Genshin, charData.Base.Id).ContinueWith(t => new PortraitResolution(null, t.Result), cancellationToken);

        await Task.WhenAll(statTask, portraitTask);

        var (baseVal, maxAscVal) = statTask.Result;
        var resolution = portraitTask.Result;

        var cardContext = new BaseCardGenerationContext<GenshinCharacterInformation>(context.UserId, charData, profile);
        cardContext.SetParameter("server", server);

        if (charData.TryGetAscensionLevelCap(baseVal, maxAscVal, out var ascLevel))
        {
            cardContext.SetParameter("ascension", ascLevel.Value);
        }
        cardContext.PortraitImageStream = resolution.ImageStream;
        cardContext.PortraitConfig = resolution.Config;

        try
        {
            using var card = await m_CardService.GetCardAsync(cardContext);
            if (!await StoreAttachmentAsync(context.UserId, filename, card))
            {
                Logger.LogError(LogMessage.AttachmentStoreError, filename, context.UserId);
                return Result<string>.Failure(StatusCode.BotError,
                    ResponseMessage.AttachmentStoreError);
            }
        }
        finally
        {
            if (resolution.ImageStream != null)
                await resolution.ImageStream.DisposeAsync();
        }

        m_MetricsService.TrackCharacterSelection(nameof(Game.Genshin), charData.Base.Name.ToLowerInvariant());

        return Result<string>.Success(filename);
    }

    private async Task<Result<string>> GetCharacterImageUrlAsync(IApplicationContext context, GameProfileDto profile,
        GenshinCharacterInformation charData, string wikiEntry, CancellationToken cancellationToken = default)
    {
        var bestStatus = StatusCode.ExternalServerError;

        var cnResult = await m_WikiApi.GetAsync(new WikiApiContext(context.UserId, Game.Genshin, wikiEntry, WikiLocales.CN), cancellationToken);
        if (cnResult.IsSuccess)
        {
            var cnUrl = cnResult.Data["data"]?["page"]?["header_img_url"]?.ToString();
            if (!string.IsNullOrEmpty(cnUrl))
                return Result<string>.Success(cnUrl);
        }

        var otherLocales = Enum.GetValues<WikiLocales>().Where(x => x != WikiLocales.CN);
        var tasks = otherLocales.Select(async locale =>
        {
            var result = await m_WikiApi.GetAsync(new WikiApiContext(context.UserId, Game.Genshin, wikiEntry, locale), cancellationToken);
            if (!result.IsSuccess) return (Result: result, Url: (string?)null);
            return (Result: result, Url: result.Data["data"]?["page"]?["header_img_url"]?.ToString());
        }).ToList();

        var results = await Task.WhenAll(tasks);
        var url = results.FirstOrDefault(x => x.Url is { Length: > 0 });

        if (url.Url == null)
        {
            foreach (var (result, _) in results)
            {
                if (result.StatusCode == StatusCode.Cancelled) bestStatus = StatusCode.Cancelled;
                else if (result.StatusCode == StatusCode.Timeout && bestStatus != StatusCode.Cancelled)
                    bestStatus = StatusCode.Timeout;
            }
            return Result<string>.Failure(bestStatus);
        }

        return Result<string>.Success(url.Url);
    }

    private async Task<Result<string>>
        GetWeaponUrlsAsync(IApplicationContext context, GameProfileDto profile,
            GenshinCharacterInformation charData, string wikiEntry, CancellationToken cancellationToken = default)
    {
        var bestStatus = StatusCode.ExternalServerError;

        var cnResult = await m_WikiApi.GetAsync(new WikiApiContext(context.UserId, Game.Genshin, wikiEntry, WikiLocales.CN), cancellationToken);
        if (cnResult.IsSuccess)
        {
            var cnUrls = ParseWeaponAscendedUrls(cnResult.Data);
            if (cnUrls.Count == 2)
                return Result<string>.Success(cnUrls[1]);
        }

        var otherLocales = Enum.GetValues<WikiLocales>().Where(x => x != WikiLocales.CN);
        var tasks = otherLocales.Select(async locale =>
        {
            var result = await m_WikiApi.GetAsync(new WikiApiContext(context.UserId, Game.Genshin, wikiEntry, locale), cancellationToken);
            if (!result.IsSuccess) return (Result: result, Parsed: (List<string>?)null);
            return (Result: result, Parsed: ParseWeaponAscendedUrls(result.Data));
        }).ToList();

        var results = await Task.WhenAll(tasks);
        var urls = results.FirstOrDefault(x => x.Parsed is { Count: 2 });

        if (urls.Parsed == null)
        {
            foreach (var (result, _) in results)
            {
                if (result.StatusCode == StatusCode.Cancelled) bestStatus = StatusCode.Cancelled;
                else if (result.StatusCode == StatusCode.Timeout && bestStatus != StatusCode.Cancelled)
                    bestStatus = StatusCode.Timeout;
            }
            return Result<string>.Failure(bestStatus);
        }

        return Result<string>.Success(urls.Parsed[1]);
    }

    private static List<string> ParseWeaponAscendedUrls(JsonNode data)
    {
        var jsonStr = data["data"]?["page"]?["modules"]?.AsArray().SelectMany(x => x?["components"]?.AsArray() ?? [])
            .FirstOrDefault(x => x?["component_id"]?.GetValue<string>() == "gallery_character")?["data"]?.GetValue<string>();

        if (string.IsNullOrEmpty(jsonStr)) return [];

        var json = JsonNode.Parse(jsonStr);
        return json?["list"]?.AsArray().Select(x => x?["img"]?.GetValue<string>())
            .Where(x => !string.IsNullOrEmpty(x)).Cast<string>().ToList() ?? [];
    }
}
