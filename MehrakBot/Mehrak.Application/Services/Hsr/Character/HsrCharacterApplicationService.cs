#region

using System.Text.Json;
using System.Text.Json.Nodes;
using Mehrak.Application.Builders;
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
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.Application.Services.Hsr.Character;

public class HsrCharacterApplicationService : BaseApplicationService<HsrCharacterApplicationContext>
{
    private readonly ICardService<HsrCharacterInformation> m_CardService;
    private readonly IApiService<JsonNode, WikiApiContext> m_WikiApi;
    private readonly IImageUpdaterService m_ImageUpdaterService;
    private readonly IImageRepository m_ImageRepository;
    private readonly ICharacterCacheService m_CharacterCacheService;

    private readonly ICharacterApiService<HsrBasicCharacterData, HsrCharacterInformation, CharacterApiContext>
        m_CharacterApi;

    private readonly IMetricsService m_MetricsService;
    private readonly IRelicRepository m_RelicRepository;

    public HsrCharacterApplicationService(
        ICardService<HsrCharacterInformation> cardService,
        IApiService<JsonNode, WikiApiContext> wikiApi,
        IImageUpdaterService imageUpdaterService,
        IImageRepository imageRepository,
        ICharacterCacheService characterCacheService,
        ICharacterApiService<HsrBasicCharacterData, HsrCharacterInformation, CharacterApiContext> characterApi,
        IMetricsService metricsService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        IUserRepository userRepository,
        IRelicRepository relicRepository,
        ILogger<HsrCharacterApplicationService> logger) : base(gameRoleApi, userRepository, logger)
    {
        m_CardService = cardService;
        m_WikiApi = wikiApi;
        m_ImageUpdaterService = imageUpdaterService;
        m_ImageRepository = imageRepository;
        m_CharacterCacheService = characterCacheService;
        m_CharacterApi = characterApi;
        m_MetricsService = metricsService;
        m_RelicRepository = relicRepository;
    }

    public override async Task<CommandResult> ExecuteAsync(HsrCharacterApplicationContext context)
    {
        var characterName = context.GetParameter<string>("character")!;

        try
        {
            var server = Enum.Parse<Server>(context.GetParameter<string>("server")!);
            var region = server.ToRegion();

            var profile = await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.HonkaiStarRail,
                region);

            if (profile == null)
            {
                Logger.LogWarning(LogMessage.InvalidLogin, context.UserId);
                return CommandResult.Failure(CommandFailureReason.AuthError, ResponseMessage.AuthError);
            }

            await UpdateGameUidAsync(context.UserId, context.LtUid, Game.HonkaiStarRail, profile.GameUid, server);

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

            var characterInfo = characterList.AvatarList.FirstOrDefault(x =>
                x.Name!.Equals(characterName, StringComparison.OrdinalIgnoreCase));

            if (characterInfo == null)
            {
                m_CharacterCacheService.GetAliases(Game.HonkaiStarRail).TryGetValue(characterName, out var alias);
                if (alias == null ||
                    (characterInfo = characterList.AvatarList?.FirstOrDefault(x =>
                        x.Name!.Equals(alias, StringComparison.OrdinalIgnoreCase))) == null)
                {
                    Logger.LogWarning(LogMessage.CharNotFoundInfo, characterName, context.UserId, gameUid);
                    return CommandResult.Success([
                        new CommandText(
                            string.Format(ResponseMessage.CharacterNotFound, characterName))
                    ], isEphemeral: true);
                }
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

                    for (int i = start; i <= end; i++)
                    {
                        if (characterList.RelicWiki.ContainsKey(x.Id.ToString()) &&
                            !await m_ImageRepository.FileExistsAsync(
                                string.Format(FileNameFormat.Hsr.FileName, $"{setId}{i}")))
                            return true;
                    }

                    return false;
                })
                .ToDictionaryAsync(async (x, token) => await Task.FromResult(x),
                    async (x, token) =>
                    {
                        var url = characterList.RelicWiki[x.Id.ToString()];
                        var entryPage = url.Split('/')[^1];
                        string? jsonStr = null;

                        foreach (var locale in Enum.GetValues<WikiLocales>())
                        {
                            var wikiResponse =
                            await m_WikiApi.GetAsync(new WikiApiContext(context.UserId, Game.HonkaiStarRail,
                                entryPage, locale));
                            if (!wikiResponse.IsSuccess) return null;

                            if (locale == WikiLocales.EN)
                            {
                                var setName = wikiResponse.Data["data"]?["page"]?["name"]?.GetValue<string>();
                                if (setName != null) await m_RelicRepository.AddSetName(x.GetSetId(), setName);
                            }

                            jsonStr = wikiResponse.Data["data"]?["page"]?["modules"]?.AsArray()
                                .FirstOrDefault(x => x?["name"]?.GetValue<string>() == "Set")?
                                ["components"]?.AsArray()[0]?["data"]?.GetValue<string>();

                            if (!string.IsNullOrEmpty(jsonStr)) break;
                        }

                        if (string.IsNullOrEmpty(jsonStr)) return null;

                        return JsonNode.Parse(jsonStr)?["list"]?.AsArray();
                    });

            var uniqueRelicSetId = uniqueRelicSet.Where(x => x.Value != null).Select(x => x.Key.GetSetId()).ToHashSet();

            List<Task<bool>> tasks = [];

            var relicProcessor = new ImageProcessorBuilder().Resize(150, 0).AddOperation(x => x.ApplyGradientFade(0.5f)).Build();

            tasks.Add(m_ImageUpdaterService.UpdateImageAsync(characterInfo.ToImageData(),
                new ImageProcessorBuilder().Resize(1000, 0).Build()));
            tasks.AddRange(uniqueRelicSet.Where(x => x.Value != null).SelectMany(x => x.Value!.Select((e, i) =>
                    new ImageData(
                        string.Format(FileNameFormat.Hsr.FileName, $"{x.Key.GetSetId()}{(x.Key.Pos < 5 ? i + 1 : i + 5)}"),
                        e?["icon_url"]?.GetValue<string>() ?? string.Empty))
                .Select(x => m_ImageUpdaterService.UpdateImageAsync(x, relicProcessor))));
            tasks.AddRange(characterInfo.Relics.Concat(characterInfo.Ornaments)
                .Where(x => !uniqueRelicSetId.Contains(x.GetSetId()))
                .Select(r => new ImageData(r.ToImageName(), r.Icon))
                .Select(x => m_ImageUpdaterService.UpdateImageAsync(x, relicProcessor)));

            if (characterInfo.Equip != null &&
                characterList.EquipWiki.TryGetValue(characterInfo.Equip.Id.ToString(), out var wikiEntry) &&
                !await m_ImageRepository.FileExistsAsync(string.Format(FileNameFormat.Hsr.FileName,
                    characterInfo.Equip.Id)))
            {
                var entryPage = wikiEntry.Split('/')[^1];
                var wikiResponse =
                    await m_WikiApi.GetAsync(new WikiApiContext(context.UserId, Game.HonkaiStarRail, entryPage));

                if (!wikiResponse.IsSuccess)
                {
                    Logger.LogError(LogMessage.ApiError, "Equip Wiki", context.UserId, gameUid, wikiResponse);
                    return CommandResult.Failure(CommandFailureReason.ApiError,
                        string.Format(ResponseMessage.ApiError, "Light Cone Data"));
                }

                string? iconUrl = wikiResponse.Data["data"]?["page"]?["icon_url"]?.GetValue<string>();

                if (iconUrl == null)
                {
                    Logger.LogError(LogMessage.ApiError, "Equip Wiki", context.UserId, gameUid,
                        "Failed to retrieve Icon Url");
                    return CommandResult.Failure(CommandFailureReason.ApiError,
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
                return CommandResult.Failure(CommandFailureReason.ApiError, ResponseMessage.ImageUpdateError);
            }

            var cardContext = new BaseCardGenerationContext<HsrCharacterInformation>(context.UserId, characterInfo, profile);
            cardContext.SetParameter("server", server);

            var card = await m_CardService.GetCardAsync(cardContext);

            m_MetricsService.TrackCharacterSelection(nameof(Game.HonkaiStarRail),
                characterInfo.Name.ToLowerInvariant());

            return CommandResult.Success([
                new CommandText($"<@{context.UserId}>"), new CommandAttachment("character_card.jpg", card)
            ]);
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
}
