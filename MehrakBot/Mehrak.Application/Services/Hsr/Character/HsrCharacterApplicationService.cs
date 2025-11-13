#region

using System.Text.Json;
using System.Text.Json.Nodes;
using Mehrak.Application.Builders;
using Mehrak.Application.Services.Common;
using Mehrak.Application.Services.Genshin.Types;
using Mehrak.Application.Utility;
using Mehrak.Domain.Common;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.Domain.Utility;
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
        ILogger<HsrCharacterApplicationService> logger) : base(gameRoleApi, userRepository, logger)
    {
        m_CardService = cardService;
        m_WikiApi = wikiApi;
        m_ImageUpdaterService = imageUpdaterService;
        m_ImageRepository = imageRepository;
        m_CharacterCacheService = characterCacheService;
        m_CharacterApi = characterApi;
        m_MetricsService = metricsService;
    }

    public override async Task<CommandResult> ExecuteAsync(HsrCharacterApplicationContext context)
    {
        var characterName = context.GetParameter<string>("character")!;

        try
        {
            var region = context.Server.ToRegion();

            var profile = await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.HonkaiStarRail,
                region);

            if (profile == null)
            {
                Logger.LogWarning(LogMessage.InvalidLogin, context.UserId);
                return CommandResult.Failure(CommandFailureReason.AuthError, ResponseMessage.AuthError);
            }

            await UpdateGameUidAsync(context.UserId, context.LtUid, Game.HonkaiStarRail, profile.GameUid, context.Server);

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
                .Where(async (x, token) => characterList.RelicWiki.ContainsKey(x.Id.ToString()) &&
                                       !await m_ImageRepository.FileExistsAsync(
                                           string.Format(FileNameFormat.Hsr.FileName, x.Id)))
                .ToDictionaryAsync(async (x, token) => await Task.FromResult(x.GetSetId()),
                    async (x, token) =>
                    {
                        var url = characterList.RelicWiki[x.Id.ToString()];
                        var entryPage = url.Split('/')[^1];
                        var wikiResponse =
                            await m_WikiApi.GetAsync(new WikiApiContext(context.UserId, Game.HonkaiStarRail,
                                entryPage));
                        if (!wikiResponse.IsSuccess) return null;

                        var jsonStr = wikiResponse.Data["data"]?["page"]?["modules"]?.AsArray()
                            .FirstOrDefault(x => x?["name"]?.GetValue<string>() == "Set")?
                            ["components"]?.AsArray()[0]?["data"]?.GetValue<string>();
                        if (jsonStr == null) return null;

                        return JsonNode.Parse(jsonStr)?["list"]?.AsArray();
                    });

            List<Task<bool>> tasks = [];

            tasks.Add(m_ImageUpdaterService.UpdateImageAsync(characterInfo.ToImageData(),
                new ImageProcessorBuilder().Resize(1000, 0).Build()));
            tasks.AddRange(characterInfo.Relics.Concat(characterInfo.Ornaments).Select(r =>
            {
                var setId = r.GetSetId();
                if (uniqueRelicSet.TryGetValue(setId, out var jsonArray) && jsonArray != null)
                {
                    string? iconUrl = jsonArray
                        .FirstOrDefault(x => r.Name.Equals(RegexExpressions.QuotationMarkRegex()
                            .Replace(x?["name"]?.GetValue<string>() ?? "", "'"), StringComparison.OrdinalIgnoreCase))
                        ?["icon_url"]?.GetValue<string>();

                    if (iconUrl != null)
                        return new ImageData(string.Format(FileNameFormat.Hsr.FileName, r.Id), iconUrl);
                }

                return new ImageData(string.Format(FileNameFormat.Hsr.FileName, r.Id), r.Icon);
            }).Select(x => m_ImageUpdaterService.UpdateImageAsync(x,
                new ImageProcessorBuilder().Resize(150, 0).AddOperation(x => x.ApplyGradientFade(0.5f)).Build())));

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

            var card = await m_CardService.GetCardAsync(
                new BaseCardGenerationContext<HsrCharacterInformation>(context.UserId, characterInfo, context.Server,
                    profile));

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
