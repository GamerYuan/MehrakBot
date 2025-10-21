using Mehrak.Application.Builders;
using Mehrak.Application.Services.Common;
using Mehrak.Application.Services.Genshin.Types;
using Mehrak.Domain.Common;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Zzz.Types;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;

namespace Mehrak.Application.Services.Zzz.Character;

internal class ZzzCharacterApplicationService : BaseApplicationService<ZzzCharacterApplicationContext>
{
    private readonly ICardService<ZzzFullAvatarData> m_CardService;
    private readonly IImageUpdaterService m_ImageUpdaterService;
    private readonly IImageRepository m_ImageRepository;
    private readonly ICharacterApiService<ZzzBasicAvatarData, ZzzFullAvatarData, CharacterApiContext> m_CharacterApi;
    private readonly ICharacterCacheService m_CharacterCacheService;
    private readonly IApiService<JsonNode, WikiApiContext> m_WikiApi;

    public ZzzCharacterApplicationService(
        ICardService<ZzzFullAvatarData> cardService,
        IImageUpdaterService imageUpdaterService,
        IImageRepository imageRepository,
        ICharacterApiService<ZzzBasicAvatarData, ZzzFullAvatarData, CharacterApiContext> characterApi,
        ICharacterCacheService characterCacheService,
        IApiService<JsonNode, WikiApiContext> wikiApi,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        ILogger<ZzzCharacterApplicationService> logger)
        : base(gameRoleApi, logger)
    {
        m_CardService = cardService;
        m_ImageUpdaterService = imageUpdaterService;
        m_ImageRepository = imageRepository;
        m_CharacterApi = characterApi;
        m_CharacterCacheService = characterCacheService;
        m_WikiApi = wikiApi;
    }

    public override async Task<CommandResult> ExecuteAsync(ZzzCharacterApplicationContext context)
    {
        string characterName = context.GetParameter<string>("character")!;

        try
        {
            string region = context.Server.ToRegion();

            var profile = await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.ZenlessZoneZero, region);

            if (profile == null)
            {
                Logger.LogWarning("No profile found for user {UserId}", context.UserId);
                return CommandResult.Failure("Invalid HoYoLAB UID or Cookies. Please authenticate again");
            }

            string gameUid = profile.GameUid;

            var charResponse = await m_CharacterApi.GetAllCharactersAsync(new(context.UserId, context.LtUid, context.LToken, gameUid, region));

            if (!charResponse.IsSuccess)
            {
                Logger.LogInformation("No character data found for user {UserId} on {Region} server",
                    context.UserId, region);
                return CommandResult.Failure("Failed to fetch character list. Please try again later.");
            }

            var characters = charResponse.Data;

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
                    Logger.LogInformation("Character {CharacterName} not found for user {UserId} on {Region} server",
                        characterName, context.UserId, region);
                    return CommandResult.Failure($"Character {characterName} not found. Please try again");
                }
            }

            Result<ZzzFullAvatarData> response = await
                m_CharacterApi.GetCharacterDetailAsync(new(context.UserId, context.LtUid, context.LToken, gameUid, region, character.Id!));

            if (!response.IsSuccess)
            {
                Logger.LogInformation("Failed to fetch character detail for gameUid: {GameUid}, characterId: {CharacterId}, error: {Error}",
                    profile.GameUid, character.Id, response.ErrorMessage);
                return CommandResult.Failure("Failed to retrieve character detail. Please try again later");
            }

            ZzzFullAvatarData characterData = response.Data;
            ZzzAvatarData charInfo = characterData.AvatarList[0];

            List<Task> tasks = [];

            if (!await m_ImageRepository.FileExistsAsync(string.Format(FileNameFormat.Zzz.FileName, charInfo.Id)))
            {
                var entryPage = characterData.AvatarWiki[charInfo.Id.ToString()].Split('/')[^1];
                var wikiResponse = await m_WikiApi.GetAsync(new(context.UserId, Game.ZenlessZoneZero, entryPage));

                if (!wikiResponse.IsSuccess)
                {
                    Logger.LogInformation("Failed to fetch wiki information for character {Character}", charInfo.Name);
                    return CommandResult.Failure("Unable to retrieve character image");
                }

                var url = JsonNode.Parse(wikiResponse.Data["data"]?["page"]?["modules"]?.AsArray().FirstOrDefault(x => x?["name"]?
                    .GetValue<string>() == "Gallery")?["components"]?[0]?["data"]?.GetValue<string>() ?? "")
                    ?["list"]?.AsArray().FirstOrDefault(x => x?["key"]?.GetValue<string>() == "Splash Art")?["img"]?.GetValue<string>();

                if (string.IsNullOrEmpty(url))
                {
                    Logger.LogInformation("Failed to fetch wiki information for character {Character}", charInfo.Name);
                    return CommandResult.Failure("Unable to retrieve character image");
                }

                tasks.Add(m_ImageUpdaterService.UpdateImageAsync(new ImageData(string.Format(FileNameFormat.Zzz.FileName, charInfo.Id),
                    url), new ImageProcessorBuilder().Resize(2000, 0).Build()));
            }

            if (charInfo.Weapon != null)
            {
                tasks.Add(m_ImageUpdaterService.UpdateImageAsync(charInfo.Weapon.ToImageData(),
                    new ImageProcessorBuilder().Resize(150, 0).Build()));
            }

            tasks.AddRange(charInfo.Equip.DistinctBy(x => x.EquipSuit)
                .Select(x => m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(), new ImageProcessorBuilder().Resize(140, 0).Build())));

            await Task.WhenAll(tasks);

            var card = await m_CardService.GetCardAsync(
                new BaseCardGenerationContext<ZzzFullAvatarData>(context.UserId, characterData, context.Server, profile));

            return CommandResult.Success([new CommandText($"<@{context.UserId}>"), new CommandAttachment("character_card.jpg", card)]);
        }
        catch (CommandException e)
        {
            Logger.LogError(e, "Error sending character card response with character {CharacterName} for user {UserId}",
                characterName, context.UserId);
            return CommandResult.Failure(e.Message);
        }
        catch (Exception e)
        {
            Logger.LogError(e,
                "Error sending character card response with character {CharacterName} for user {UserId}",
                characterName, context.UserId);
            return CommandResult.Failure("An error occurred while processing your request");
        }
    }
}
