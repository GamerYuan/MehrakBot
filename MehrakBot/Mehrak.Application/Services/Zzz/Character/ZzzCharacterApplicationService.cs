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
using Mehrak.GameApi.Zzz.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.Application.Services.Zzz.Character;

internal class ZzzCharacterApplicationService : BaseApplicationService<ZzzCharacterApplicationContext>
{
    private readonly ICardService<ZzzFullAvatarData> m_CardService;
    private readonly IImageUpdaterService m_ImageUpdaterService;
    private readonly IImageRepository m_ImageRepository;
    private readonly ICharacterApiService<ZzzBasicAvatarData, ZzzFullAvatarData, CharacterApiContext> m_CharacterApi;
    private readonly ICharacterCacheService m_CharacterCacheService;
    private readonly IApiService<JsonNode, WikiApiContext> m_WikiApi;
    private readonly IMetricsService m_MetricsService;

    public ZzzCharacterApplicationService(
        ICardService<ZzzFullAvatarData> cardService,
        IImageUpdaterService imageUpdaterService,
        IImageRepository imageRepository,
        ICharacterApiService<ZzzBasicAvatarData, ZzzFullAvatarData, CharacterApiContext> characterApi,
        ICharacterCacheService characterCacheService,
        IApiService<JsonNode, WikiApiContext> wikiApi,
        IMetricsService metricsService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        IUserRepository userRepository,
        ILogger<ZzzCharacterApplicationService> logger)
        : base(gameRoleApi, userRepository, logger)
    {
        m_CardService = cardService;
        m_ImageUpdaterService = imageUpdaterService;
        m_ImageRepository = imageRepository;
        m_CharacterApi = characterApi;
        m_CharacterCacheService = characterCacheService;
        m_WikiApi = wikiApi;
        m_MetricsService = metricsService;
    }

    public override async Task<CommandResult> ExecuteAsync(ZzzCharacterApplicationContext context)
    {
        string characterName = context.GetParameter<string>("character")!;

        try
        {
            var server = Enum.Parse<Server>(context.GetParameter<string>("server")!);
            string region = server.ToRegion();

            var profile = await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.ZenlessZoneZero,
                region);

            if (profile == null)
            {
                Logger.LogWarning(LogMessage.InvalidLogin, context.UserId);
                return CommandResult.Failure(CommandFailureReason.AuthError, ResponseMessage.AuthError);
            }

            await UpdateGameUidAsync(context.UserId, context.LtUid, Game.ZenlessZoneZero, profile.GameUid, server);

            string gameUid = profile.GameUid;

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

            ZzzBasicAvatarData? character = characters.FirstOrDefault(x =>
                x.Name.Equals(characterName, StringComparison.OrdinalIgnoreCase) ||
                x.FullName.Equals(characterName, StringComparison.OrdinalIgnoreCase));

            if (character == null)
            {
                m_CharacterCacheService.GetAliases(Game.ZenlessZoneZero).TryGetValue(characterName, out string? name);

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

            Result<ZzzFullAvatarData> response = await
                m_CharacterApi.GetCharacterDetailAsync(new CharacterApiContext(context.UserId, context.LtUid,
                    context.LToken, gameUid, region, character.Id!));

            if (!response.IsSuccess)
            {
                Logger.LogError(LogMessage.ApiError, "Character", context.UserId, gameUid, response);
                return CommandResult.Failure(CommandFailureReason.ApiError,
                    string.Format(ResponseMessage.ApiError, "Character data"));
            }

            ZzzFullAvatarData characterData = response.Data;
            ZzzAvatarData charInfo = characterData.AvatarList[0];

            List<Task<bool>> tasks = [];

            if (!await m_ImageRepository.FileExistsAsync(string.Format(FileNameFormat.Zzz.FileName, charInfo.Id)))
            {
                var entryPage = characterData.AvatarWiki[charInfo.Id.ToString()].Split('/')[^1];
                var wikiResponse =
                    await m_WikiApi.GetAsync(new WikiApiContext(context.UserId, Game.ZenlessZoneZero, entryPage));

                if (!wikiResponse.IsSuccess)
                {
                    Logger.LogWarning(LogMessage.ApiError, "Character Wiki", context.UserId, gameUid, wikiResponse);
                    return CommandResult.Failure(CommandFailureReason.ApiError,
                        string.Format(ResponseMessage.ApiError, "Character Image"));
                }

                var url = JsonNode.Parse(wikiResponse.Data["data"]?["page"]?["modules"]?.AsArray().FirstOrDefault(x =>
                        x?["name"]?
                            .GetValue<string>() == "Gallery")?["components"]?[0]?["data"]?.GetValue<string>() ?? "")
                    ?["list"]?.AsArray().FirstOrDefault()?["img"]?.GetValue<string>();

                if (string.IsNullOrEmpty(url))
                {
                    Logger.LogError("Character wiki image URL is empty for characterId: {CharacterId}, Data:\n{Data}",
                        charInfo.Id, wikiResponse.Data.ToJsonString());
                    return CommandResult.Failure(CommandFailureReason.ApiError,
                        string.Format(ResponseMessage.ApiError, "Character Image"));
                }

                tasks.Add(m_ImageUpdaterService.UpdateImageAsync(new ImageData(charInfo.ToImageName(),
                    url), new ImageProcessorBuilder().Resize(2000, 0).Build()));
            }

            if (charInfo.Weapon != null)
                tasks.Add(m_ImageUpdaterService.UpdateImageAsync(charInfo.Weapon.ToImageData(),
                    new ImageProcessorBuilder().Resize(150, 0).Build()));

            tasks.AddRange(charInfo.Equip.DistinctBy(x => x.EquipSuit)
                .Select(x =>
                    m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(),
                        new ImageProcessorBuilder().Resize(140, 0).Build())));

            var completed = await Task.WhenAll(tasks);

            if (completed.Any(x => !x))
            {
                Logger.LogError(LogMessage.ImageUpdateError, "Character", context.UserId,
                    JsonSerializer.Serialize(charInfo));
                return CommandResult.Failure(CommandFailureReason.ApiError, ResponseMessage.ImageUpdateError);
            }

            var cardContext = new BaseCardGenerationContext<ZzzFullAvatarData>(context.UserId, characterData, profile);
            cardContext.SetParameter("server", server);

            var card = await m_CardService.GetCardAsync(cardContext);

            m_MetricsService.TrackCharacterSelection(nameof(Game.ZenlessZoneZero), charInfo.Name.ToLowerInvariant());

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
