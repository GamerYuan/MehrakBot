#region

using System.Text.Json;
using System.Text.Json.Nodes;
using Mehrak.Application.Builders;
using Mehrak.Application.Services.Abstractions;
using Mehrak.Application.Services.Common;
using Mehrak.Application.Services.Common.Types;
using Mehrak.Application.Utility;
using Mehrak.Domain.Common;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Hsr.Types;
using Mehrak.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

#endregion

namespace Mehrak.Application.Services.Hsr.Character;

public class HsrCharacterApplicationService : BaseAttachmentApplicationService
{
    private const int MaxRequestCount = 4;

    private readonly ICardService<HsrCharacterInformation> m_CardService;
    private readonly IApiService<JsonNode, WikiApiContext> m_WikiApi;
    private readonly IImageUpdaterService m_ImageUpdaterService;
    private readonly IImageRepository m_ImageRepository;
    private readonly ICharacterCacheService m_CharacterCacheService;
    private readonly IAliasService m_AliasService;

    private readonly ICharacterApiService<HsrBasicCharacterData, HsrCharacterInformation, CharacterApiContext>
        m_CharacterApi;

    private readonly IApplicationMetrics m_MetricsService;
    private readonly RelicDbContext m_RelicContext;

    public HsrCharacterApplicationService(
        ICardService<HsrCharacterInformation> cardService,
        IApiService<JsonNode, WikiApiContext> wikiApi,
        IImageUpdaterService imageUpdaterService,
        IImageRepository imageRepository,
        ICharacterCacheService characterCacheService,
        IAliasService aliasService,
        ICharacterApiService<HsrBasicCharacterData, HsrCharacterInformation, CharacterApiContext> characterApi,
        IApplicationMetrics metricsService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        UserDbContext userContext,
        RelicDbContext relicContext,
        IAttachmentStorageService attachmentStorageService,
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
        m_RelicContext = relicContext;
    }

    public override async Task<CommandResult> ExecuteAsync(IApplicationContext context)
    {
        try
        {
            var server = Enum.Parse<Server>(context.GetParameter("server")!);
            var region = server.ToRegion();
            var input = context.GetParameter("character")!.Split(',');

            if (input.Length > MaxRequestCount)
            {
                return CommandResult.Success(
                    [new CommandText("Exceeded the maximum number of characters per request! (Max 4)")],
                    isEphemeral: true);
            }

            var profile = await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.HonkaiStarRail,
                region);

            if (profile == null)
            {
                Logger.LogWarning(LogMessage.InvalidLogin, context.UserId);
                return CommandResult.Failure(CommandFailureReason.AuthError, ResponseMessage.AuthError);
            }

            await UpdateGameUidAsync(context.UserId, context.LtUid, Game.HonkaiStarRail, profile.GameUid, server.ToString());

            var gameUid = profile.GameUid;

            var charResponse = await m_CharacterApi.GetAllCharactersAsync(
                new CharacterApiContext(context.UserId, context.LtUid, context.LToken, gameUid, region));

            if (!charResponse.IsSuccess)
            {
                Logger.LogError(LogMessage.ApiError, "Character", context.UserId, gameUid, charResponse);
                return CommandResult.Failure(CommandFailureReason.ApiError,
                    string.Format(ResponseMessage.ApiError, "Character data"));
            }

            var characterList = charResponse.Data.First();
            _ = m_CharacterCacheService.UpsertCharacters(Game.HonkaiStarRail,
                characterList.AvatarList.Select(x => x.Name));

            var names = characterList.AvatarList.ToDictionary(x => x.Name, x => x, StringComparer.OrdinalIgnoreCase);
            Dictionary<int, HsrCharacterInformation> validCharacters = [];
            List<string> failureMessages = [];

            foreach (var c in input)
            {
                var characterName = c.Trim();
                var character = names.GetValueOrDefault(characterName);

                if (character == null)
                {
                    m_AliasService.GetAliases(Game.HonkaiStarRail).TryGetValue(characterName, out var name);

                    if (name == null ||
                        (character = names.GetValueOrDefault(name)) == null)
                    {
                        Logger.LogInformation(LogMessage.CharNotFoundInfo, characterName, context.UserId, gameUid);
                        failureMessages.Add(string.Format(ResponseMessage.CharacterNotFound, characterName));
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

            foreach (var charData in validCharacters.Values)
            {
                var result = await ProcessCharacterAsync(context, server, profile, charData,
                    characterList.RelicWiki, characterList.EquipWiki);
                if (result.IsSuccess)
                {
                    attachments.Add(result.Data);
                }
                else
                {
                    failureMessages.Add($"{charData.Name}: {result.ErrorMessage}");
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
        catch (CommandException e)
        {
            Logger.LogError(e, LogMessage.UnknownError, "Character", context.UserId, e.Message);
            return CommandResult.Failure(CommandFailureReason.BotError,
                string.Format(ResponseMessage.CardGenError, "Character"));
        }
        catch (Exception e)
        {
            Logger.LogError(e, LogMessage.UnknownError, "Character", context.UserId, e.Message);
            return CommandResult.Failure(CommandFailureReason.Unknown, ResponseMessage.UnknownError);
        }
    }

    private async Task<Result<string>> ProcessCharacterAsync(
        IApplicationContext context, Server server,
        GameProfileDto profile, HsrCharacterInformation characterInfo,
        Dictionary<string, string> relicWiki, Dictionary<string, string> equipWiki
    )
    {
        var fileName = GetFileName(JsonSerializer.Serialize(characterInfo), "jpg", profile.GameUid);
        if (await AttachmentExistsAsync(fileName))
        {
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

                var setId = x.GetSetId();

                for (var i = start; i <= end; i++)
                {
                    if (relicWiki.ContainsKey(x.Id.ToString()) &&
                        !await m_ImageRepository.FileExistsAsync(string.Format(FileNameFormat.Hsr.FileName, $"{setId}{i}"), token))
                        return true;
                }

                return false;
            })
            .ToDictionaryAsync(async (x, token) => await Task.FromResult(x),
                async (x, token) =>
                {
                    var url = relicWiki[x.Id.ToString()];
                    var entryPage = url.Split('/')[^1];
                    string? jsonStr = null;

                    var setId = x.GetSetId();

                    foreach (var locale in Enum.GetValues<WikiLocales>())
                    {
                        var wikiResponse =
                            await m_WikiApi.GetAsync(new WikiApiContext(context.UserId, Game.HonkaiStarRail,
                                entryPage, locale));
                        if (!wikiResponse.IsSuccess)
                        {
                            Logger.LogWarning(LogMessage.ApiError, "Relic Wiki", context.UserId, profile.GameUid, wikiResponse);
                            continue;
                        }

                        if (locale == WikiLocales.EN)
                        {
                            var setName = wikiResponse.Data["data"]?["page"]?["name"]?.GetValue<string>();
                            if (setName != null) await AddSetName(setId, setName);
                        }

                        jsonStr = wikiResponse.Data["data"]?["page"]?["modules"]?.AsArray()
                            .SelectMany(x => x?["components"]?.AsArray() ?? [])
                            .FirstOrDefault(x => x?["component_id"]?.GetValue<string>() == "set")
                            ?["data"]?.GetValue<string>();

                        if (!string.IsNullOrEmpty(jsonStr)) break;

                        Logger.LogWarning("Character wiki image URL is empty for RelicId: {RelicId}, Locale: {Locale}, Data:\n{Data}",
                            setId, locale, wikiResponse.Data.ToJsonString());
                    }

                    if (string.IsNullOrEmpty(jsonStr)) return null;

                    return JsonNode.Parse(jsonStr)?["list"]?.AsArray();
                });

        var uniqueRelicSetId = uniqueRelicSet.Where(x => x.Value != null).Select(x => x.Key.GetSetId()).ToHashSet();

        List<Task<bool>> tasks = [];

        var relicProcessor = new ImageProcessorBuilder().Resize(150, 0).AddOperation(x => x.ApplyGradientFade(0.5f)).Build();

        tasks.Add(m_ImageUpdaterService.UpdateImageAsync(characterInfo.ToImageData(),
            new ImageProcessorBuilder().Resize(1000, 0).Build()));
        tasks.AddRange(uniqueRelicSet.Where(x => x.Value != null)
            .SelectMany(x => x.Value!.Select((e, i) =>
                new ImageData(
                    string.Format(FileNameFormat.Hsr.FileName, $"{x.Key.GetSetId()}{(x.Key.Pos < 5 ? i + 1 : i + 5)}"),
                    e?["icon_url"]?.GetValue<string>() ?? string.Empty))
            .Select(x => m_ImageUpdaterService.UpdateImageAsync(x, relicProcessor))));
        tasks.AddRange(characterInfo.Relics.Concat(characterInfo.Ornaments)
            .Where(x => !uniqueRelicSetId.Contains(x.GetSetId()))
            .Select(r => new ImageData(r.ToImageName(), r.Icon))
            .Select(x => m_ImageUpdaterService.UpdateImageAsync(x, relicProcessor)));

        if (characterInfo.Equip != null &&
            equipWiki.TryGetValue(characterInfo.Equip.Id.ToString(), out var wikiEntry) &&
            !await m_ImageRepository.FileExistsAsync(string.Format(FileNameFormat.Hsr.FileName,
                characterInfo.Equip.Id)))
        {
            var entryPage = wikiEntry.Split('/')[^1];
            var wikiResponse =
                await m_WikiApi.GetAsync(new WikiApiContext(context.UserId, Game.HonkaiStarRail, entryPage));

            if (!wikiResponse.IsSuccess)
            {
                Logger.LogError(LogMessage.ApiError, "Equip Wiki", context.UserId, profile.GameUid, wikiResponse);
                return Result<string>.Failure(StatusCode.ExternalServerError,
                    string.Format(ResponseMessage.ApiError, "Light Cone Data"));
            }

            var iconUrl = wikiResponse.Data["data"]?["page"]?["icon_url"]?.GetValue<string>();

            if (iconUrl == null)
            {
                Logger.LogError(LogMessage.ApiError, "Equip Wiki", context.UserId, profile.GameUid,
                    "Failed to retrieve Icon Url");
                return Result<string>.Failure(StatusCode.ExternalServerError,
                    string.Format(ResponseMessage.ApiError, "Light Cone Data"));
            }

            tasks.Add(m_ImageUpdaterService.UpdateImageAsync(
                new ImageData(string.Format(FileNameFormat.Hsr.FileName, characterInfo.Equip.Id), iconUrl),
                new ImageProcessorBuilder().Resize(300, 0).Build()));
        }

        tasks.AddRange(characterInfo.Skills.Select(x => m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(),
            new ImageProcessorBuilder().Resize(x.PointType == 1 ? 50 : 80, 0).Build())));

        if (characterInfo.ServantDetail != null)
        {
            tasks.AddRange(characterInfo.ServantDetail.ServantSkills?.Select(x =>
                m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(),
                    new ImageProcessorBuilder().Resize(80, 0).Build())) ?? []);
        }

        tasks.AddRange(characterInfo.Ranks.Select(x => m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(),
            new ImageProcessorBuilder().Resize(80, 0).Build())));

        var completed = await Task.WhenAll(tasks);

        if (completed.Any(x => !x))
        {
            Logger.LogError(LogMessage.ImageUpdateError, "Character", context.UserId,
                JsonSerializer.Serialize(characterInfo));
            return Result<string>.Failure(StatusCode.ExternalServerError, ResponseMessage.ImageUpdateError);
        }

        var cardContext = new BaseCardGenerationContext<HsrCharacterInformation>(context.UserId, characterInfo, profile);
        cardContext.SetParameter("server", server);

        await using var card = await m_CardService.GetCardAsync(cardContext);

        if (!await StoreAttachmentAsync(context.UserId, fileName, card))
        {
            Logger.LogError(LogMessage.AttachmentStoreError, fileName, context.UserId);
            return Result<string>.Failure(StatusCode.BotError,
                ResponseMessage.AttachmentStoreError);
        }

        m_MetricsService.TrackCharacterSelection(nameof(Game.HonkaiStarRail),
            characterInfo.Name.ToLowerInvariant());

        return Result<string>.Success(fileName);
    }
    private async Task AddSetName(int setId, string setName)
    {
        try
        {
            var existing = await m_RelicContext.HsrRelics.AsNoTracking().FirstOrDefaultAsync(x => x.SetId == setId);
            if (existing == null)
            {
                var entity = new HsrRelicModel { SetId = setId, SetName = setName };
                m_RelicContext.HsrRelics.Add(entity);
                await m_RelicContext.SaveChangesAsync();
                Logger.LogInformation("Inserted relic set mapping: setId {SetId} -> {SetName}", setId, setName);
            }
            else
            {
                Logger.LogDebug("Relic set mapping for setId {SetId} : {SetName} already exists; skipping overwrite", setId, setName);
            }
        }
        catch (DbUpdateException e)
        {
            Logger.LogWarning(e, "An error occurred while inserting relic {SetId}, {SetName}", setId, setName);
        }
    }
}
