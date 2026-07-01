#region

using System.Text.Json;
using System.Text.Json.Nodes;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Builders;
using Mehrak.Application.Shared.Models;
using Mehrak.Application.Shared.Services;
using Mehrak.Application.Shared.Services.Types;
using Mehrak.Application.Shared.Utility;
using Mehrak.Domain.Card;
using Mehrak.Domain.Character;
using Mehrak.Domain.Character.Models;
using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Image;
using Mehrak.Domain.Image.Models;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.Domain.Shared.Utility;
using Mehrak.Domain.User.Models;
using Mehrak.GameApi.GameRole;
using Mehrak.GameApi.Hsr.Types;
using Mehrak.GameApi.Shared.Types;
using Mehrak.GameApi.Wiki;
using Mehrak.Infrastructure.Relic;
using Mehrak.Infrastructure.User;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

#endregion

namespace Mehrak.Application.Hsr.Character;

public class HsrCharacterApplicationService : BaseAttachmentApplicationService
{
    private const int MaxRequestCount = 4;

    private readonly ICardService<HsrCharacterInformation> m_CardService;
    private readonly IApiService<JsonNode, WikiApiContext> m_WikiApi;
    private readonly IImageUpdaterService m_ImageUpdaterService;
    private readonly IImageRepository m_ImageRepository;
    private readonly ICharacterCacheService m_CharacterCacheService;
    private readonly IAliasService m_AliasService;

    private readonly ICharacterApiService<HsrBasicCharacterData, HsrBasicCharacterData, CharacterApiContext>
        m_CharacterApi;

    private readonly IApplicationMetrics m_MetricsService;
    private readonly IServiceScopeFactory m_ScopeFactory;
    private readonly ICharacterPortraitConfigService m_PortraitConfigService;
    private readonly IUserPortraitService m_UserPortraitService;
    private readonly IOptions<CommandDispatcherConfig> m_DispatcherConfig;


    protected override string CommandName => "HSR Character";
    protected override bool RequiresLevel => false;
    protected override string CardName => "Character";
    public HsrCharacterApplicationService(
        ICardService<HsrCharacterInformation> cardService,
        IApiService<JsonNode, WikiApiContext> wikiApi,
        IImageUpdaterService imageUpdaterService,
        IImageRepository imageRepository,
        ICharacterCacheService characterCacheService,
        IAliasService aliasService,
        ICharacterApiService<HsrBasicCharacterData, HsrBasicCharacterData, CharacterApiContext> characterApi,
        IApplicationMetrics metricsService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        UserDbContext userContext,
        IServiceScopeFactory scopeFactory,
        IAttachmentStorageService attachmentStorageService,
        ICharacterPortraitConfigService portraitConfigService,
        IUserPortraitService userPortraitService,
        IOptions<CommandDispatcherConfig> dispatcherConfig,
        ILogger<HsrCharacterApplicationService> logger) : base(gameRoleApi, userContext, attachmentStorageService, logger)
    {
        m_CardService = cardService;
        m_WikiApi = wikiApi;
        m_ImageUpdaterService = imageUpdaterService;
        m_ImageRepository = imageRepository;
        m_CharacterCacheService = characterCacheService;
        m_AliasService = aliasService;
        m_CharacterApi = characterApi;
        m_MetricsService = metricsService;
        m_ScopeFactory = scopeFactory;
        m_PortraitConfigService = portraitConfigService;
        m_UserPortraitService = userPortraitService;
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

        var charResponse = await m_CharacterApi.GetAllCharactersAsync(
            new CharacterApiContext(context.UserId, context.LtUid, context.LToken, gameUid, region), cancellationToken);

        if (!charResponse.IsSuccess)
        {
            if (charResponse.StatusCode == StatusCode.Cancelled)
                throw new OperationCanceledException(charResponse.ErrorMessage ?? "Cancelled");
            if (charResponse.StatusCode == StatusCode.Timeout)
                return CommandResult.Failure(CommandFailureReason.Timeout, ResponseMessage.TimeoutError);
            Logger.LogError(LogMessage.ApiError, "Character", context.UserId, gameUid, charResponse);
            return CommandResult.Failure(CommandFailureReason.ApiError,
                string.Format(ResponseMessage.ApiError, "Character data"));
        }

        var characterList = charResponse.Data.First();
        _ = m_CharacterCacheService.UpsertCharacters(Game.HonkaiStarRail,
            characterList.AvatarList.Select(x => new CharacterUpsertEntry(x.Name, x.Id)));

        var names = characterList.AvatarList.ToDictionary(x => x.Name, x => x, StringComparer.OrdinalIgnoreCase);
        Dictionary<int, HsrCharacterInformation> validCharacters = [];
        List<string> failureMessages = [];

        foreach (var c in input)
        {
            var character = names.GetValueOrDefault(c);

            if (character == null)
            {
                m_AliasService.GetAliases(Game.HonkaiStarRail).TryGetValue(c, out var name);

                if (name == null ||
                    (character = names.GetValueOrDefault(name)) == null)
                {
                    Logger.LogInformation(LogMessage.CharNotFoundInfo, c, context.UserId, gameUid);
                    failureMessages.Add(string.Format(ResponseMessage.CharacterNotFound, c));
                }
            }

            if (character != null)
            {
                validCharacters.TryAdd(character.Id, character);
            }
        }

        if (validCharacters.Count == 0)
        {
            return CommandResult.Success(
                [new CommandText(string.Join('\n', failureMessages))],
                isEphemeral: true);
        }

        List<string> attachments = [];

        var charList = validCharacters.Values.ToList();
        var (results, timedOut) = await CharacterBatchProcessor.ProcessAsync(
            charList,
            (charData, ct) => ProcessCharacterAsync(context, server, profile, charData,
                characterList.RelicWiki, characterList.EquipWiki, ct),
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
                failureMessages.Add($"{charList[i].Name}: {result.ErrorMessage}");
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
        GameProfileDto profile, HsrCharacterInformation characterInfo,
        Dictionary<string, string> relicWiki, Dictionary<string, string> equipWiki,
        CancellationToken cancellationToken = default
    )
    {
        var activePortrait = await PortraitResolutionHelper.GetActivePortraitAsync(
            m_UserPortraitService, context.UserId, Game.HonkaiStarRail, characterInfo.Name, cancellationToken);

        var extraData = activePortrait != null
            ? $"{activePortrait.Key}_{JsonSerializer.Serialize(activePortrait.Config)}"
            : null;
        var fileName = GetFileName(JsonSerializer.Serialize(characterInfo), "jpg", profile.GameUid, extraData);
        if (await AttachmentExistsAsync(fileName))
        {
            m_MetricsService.TrackCharacterSelection(nameof(Game.HonkaiStarRail),
                characterInfo.Name.ToLowerInvariant());
            return Result<string>.Success(fileName);
        }

        var uniqueRelicSet = await characterInfo.Relics.Concat(characterInfo.Ornaments)
            .DistinctBy(x => x.GetSetId())
            .ToAsyncEnumerable()
            .Where(async (x, token) =>
            {
                int start = 1, end = 4;
                if (x.Pos >= 5)
                {
                    start = 5;
                    end = 6;
                }

                for (var i = start; i <= end; i++)
                {
                    if (relicWiki.ContainsKey(x.Id.ToString()) &&
                        !await m_ImageRepository.FileExistsAsync(x.ToImageName(i), token))
                        return true;
                }

                return false;
            })
            .ToDictionaryAsync((x, token) => ValueTask.FromResult(x),
                async (x, token) =>
                {
                    var url = relicWiki[x.Id.ToString()];
                    var entryPage = url.Split('/')[^1];
                    string? jsonStr = null;

                    var setId = x.GetSetId();

                    var allLocales = Enum.GetValues<WikiLocales>();
                    var wikiTasks = allLocales.Select(async locale =>
                    {
                        var result = await m_WikiApi.GetAsync(new WikiApiContext(context.UserId, Game.HonkaiStarRail, entryPage, locale), cancellationToken);
                        if (!result.IsSuccess) return (Locale: locale, Data: (JsonNode?)null, Status: result.StatusCode);
                        return (Locale: locale, Data: result.Data, Status: StatusCode.OK);
                    }).ToList();

                    var wikiResults = await Task.WhenAll(wikiTasks);
                    foreach (var (locale, data, _) in wikiResults)
                    {
                        if (data == null) continue;

                        if (locale == WikiLocales.EN)
                        {
                            var setName = data["data"]?["page"]?["name"]?.GetValue<string>();
                            if (setName != null) await AddSetName(setId, setName);
                        }

                        if (string.IsNullOrEmpty(jsonStr))
                        {
                            jsonStr = data["data"]?["page"]?["modules"]?.AsArray()
                                .SelectMany(x => x?["components"]?.AsArray() ?? [])
                                .FirstOrDefault(x => x?["component_id"]?.GetValue<string>() == "set")
                                ?["data"]?.GetValue<string>();
                        }
                    }

                    if (string.IsNullOrEmpty(jsonStr))
                    {
                        var bestStatus = StatusCode.ExternalServerError;
                        foreach (var (_, _, status) in wikiResults)
                        {
                            if (status == StatusCode.Cancelled) { bestStatus = StatusCode.Cancelled; break; }
                            if (status == StatusCode.Timeout && bestStatus != StatusCode.Cancelled) bestStatus = StatusCode.Timeout;
                        }

                        if (bestStatus == StatusCode.Cancelled)
                            throw new OperationCanceledException();

                        return null;
                    }

                    return JsonNode.Parse(jsonStr)?["list"]?.AsArray();
                });

        var uniqueRelicSetId = uniqueRelicSet.Where(x => x.Value != null).Select(x => x.Key.GetSetId()).ToHashSet();

        List<Task<bool>> tasks = [];

        var relicProcessor = new ImageProcessorBuilder().Resize(150, 0).AddOperation(x => x.ApplyGradientFade(0.5f, EasingType.InQuint)).Build();

        tasks.Add(m_ImageUpdaterService.UpdateImageAsync(characterInfo.ToImageData(),
            ImageProcessors.None, cancellationToken));
        tasks.AddRange(uniqueRelicSet.Where(x => x.Value != null)
            .SelectMany(x => x.Value!.Select((e, i) =>
                new ImageData(
                    x.Key.ToImageName(x.Key.Pos < 5 ? i + 1 : i + 5),
                    e?["icon_url"]?.GetValue<string>() ?? string.Empty))
            .Select(x => m_ImageUpdaterService.UpdateImageAsync(x, relicProcessor, cancellationToken))));
        tasks.AddRange(characterInfo.Relics.Concat(characterInfo.Ornaments)
            .Where(x => !uniqueRelicSetId.Contains(x.GetSetId()))
            .Select(r => new ImageData(r.ToImageName(), r.Icon))
            .Select(x => m_ImageUpdaterService.UpdateImageAsync(x, relicProcessor, cancellationToken)));

        try
        {
            if (characterInfo.Equip != null &&
                equipWiki.TryGetValue(characterInfo.Equip.Id.ToString(), out var wikiEntry) &&
                !await m_ImageRepository.FileExistsAsync(string.Format(FileNameFormat.Hsr.EquipName,
                    characterInfo.Equip.Id)))
            {
                var entryPage = wikiEntry.Split('/')[^1];
                string? iconUrl = null;

                var allLocales = Enum.GetValues<WikiLocales>();
                var bestStatus = StatusCode.ExternalServerError;
                var equipTasks = allLocales.Select(async locale =>
                {
                    var result = await m_WikiApi.GetAsync(new WikiApiContext(context.UserId, Game.HonkaiStarRail, entryPage, locale), cancellationToken);
                    if (!result.IsSuccess)
                    {
                        if (result.StatusCode == StatusCode.Cancelled) bestStatus = StatusCode.Cancelled;
                        else if (result.StatusCode == StatusCode.Timeout && bestStatus != StatusCode.Cancelled) bestStatus = StatusCode.Timeout;
                        return (JsonNode?)null;
                    }
                    return result.Data;
                }).ToList();

                var equipResults = await Task.WhenAll(equipTasks);
                foreach (var data in equipResults)
                {
                    if (data == null) continue;
                    iconUrl = data["data"]?["page"]?["icon_url"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(iconUrl)) break;
                }

                if (string.IsNullOrEmpty(iconUrl))
                {
                    Logger.LogError(LogMessage.ApiError, "Equip Wiki", context.UserId, profile.GameUid,
                        "Failed to retrieve Icon Url");
                    return Result<string>.Failure(bestStatus,
                        string.Format(ResponseMessage.ApiError, "Light Cone Data"));
                }

                tasks.Add(m_ImageUpdaterService.UpdateImageAsync(
                    new ImageData(string.Format(FileNameFormat.Hsr.EquipName, characterInfo.Equip.Id), iconUrl),
                    new ImageProcessorBuilder().Resize(300, 0).Build(), cancellationToken));
            }

            tasks.AddRange(characterInfo.Skills.Select(x => m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(),
                new ImageProcessorBuilder().Resize(x.PointType == 1 ? 50 : 80, 0).Build(), cancellationToken)));

            if (characterInfo.ServantDetail != null)
            {
                tasks.AddRange(characterInfo.ServantDetail.ServantSkills?.Select(x =>
                    m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(),
                        new ImageProcessorBuilder().Resize(80, 0).Build(), cancellationToken)) ?? []);
            }

            tasks.AddRange(characterInfo.Ranks.Select(x => m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(),
                new ImageProcessorBuilder().Resize(80, 0).Build(), cancellationToken)));
        }
        finally
        {
            await Task.WhenAll(tasks);
        }

        var completed = tasks.Select(x => x.Result).ToArray();

        if (completed.Any(x => !x))
        {
            Logger.LogError(LogMessage.ImageUpdateError, "Character", context.UserId,
                JsonSerializer.Serialize(characterInfo));
            return Result<string>.Failure(StatusCode.ExternalServerError, ResponseMessage.ImageUpdateError);
        }

        var cardContext = new BaseCardGenerationContext<HsrCharacterInformation>(context.UserId, characterInfo, profile);
        cardContext.SetParameter("server", server);

        var resolution = activePortrait != null
            ? await PortraitResolutionHelper.ResolveActivePortraitAsync(
                m_UserPortraitService, context.UserId, activePortrait,
                () => m_PortraitConfigService.GetConfigAsync(Game.HonkaiStarRail, characterInfo.Id), cancellationToken)
            : new PortraitResolution(null,
                await m_PortraitConfigService.GetConfigAsync(Game.HonkaiStarRail, characterInfo.Id));
        cardContext.PortraitImageStream = resolution.ImageStream;
        cardContext.PortraitConfig = resolution.Config;

        try
        {
            await using var card = await m_CardService.GetCardAsync(cardContext);
            if (!await StoreAttachmentAsync(context.UserId, fileName, card))
            {
                Logger.LogError(LogMessage.AttachmentStoreError, fileName, context.UserId);
                return Result<string>.Failure(StatusCode.BotError,
                    ResponseMessage.AttachmentStoreError);
            }
        }
        finally
        {
            if (resolution.ImageStream != null)
                await resolution.ImageStream.DisposeAsync();
        }

        m_MetricsService.TrackCharacterSelection(nameof(Game.HonkaiStarRail),
            characterInfo.Name.ToLowerInvariant());

        return Result<string>.Success(fileName);
    }
    private async Task AddSetName(int setId, string setName)
    {
        const int maxRetries = 3;
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var scope = m_ScopeFactory.CreateScope();
                await using var relicContext = scope.ServiceProvider.GetRequiredService<RelicDbContext>();
                var existing = await relicContext.HsrRelics.AsNoTracking().FirstOrDefaultAsync(x => x.SetId == setId);
                if (existing == null)
                {
                    var entity = new HsrRelicModel { SetId = setId, SetName = setName };
                    relicContext.HsrRelics.Add(entity);
                    await relicContext.SaveChangesAsync();
                    Logger.LogInformation("Inserted relic set mapping: setId {SetId} -> {SetName}", setId, setName);
                }
                else
                {
                    Logger.LogDebug("Relic set mapping for setId {SetId} : {SetName} already exists; skipping overwrite", setId, setName);
                }
                return;
            }
            catch (DbUpdateException e) when (e.InnerException is PostgresException pgEx
                                              && pgEx.SqlState is "23505" or "40001" or "57014")
            {
                // 23505 = unique_violation (already inserted by concurrent request)
                // 40001 = serialization_failure, 57014 = query_canceled — retriable
                Logger.LogWarning(e, "Retriable DB error inserting relic set {Attempt}/{MaxRetries}: {SetId}", attempt, maxRetries, setId);
                if (attempt == maxRetries)
                {
                    Logger.LogWarning(e, "Failed to insert relic set after {MaxRetries} attempts: {SetId}, {SetName}", maxRetries, setId, setName);
                    return;
                }
                await Task.Delay(100 * attempt);
            }
            catch (DbUpdateException e)
            {
                Logger.LogWarning(e, "Non-retriable DB error inserting relic {SetId}, {SetName}", setId, setName);
                return;
            }
        }
    }
}
