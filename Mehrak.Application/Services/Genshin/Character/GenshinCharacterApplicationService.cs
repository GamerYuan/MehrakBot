using Mehrak.Application.Services.Common;
using Mehrak.Application.Services.Genshin.Types;
using Mehrak.Domain.Common;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Genshin.Types;
using Microsoft.Extensions.Logging;

namespace Mehrak.Application.Services.Genshin.Character;

public class GenshinCharacterApplicationService : BaseApplicationService<GenshinCharacterApplicationContext>
{
    private readonly ICardService<GenshinCharacterInformation> m_CardService;
    private readonly ICharacterCacheService m_CharacterCacheService;
    private readonly ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, CharacterApiContext> m_CharacterApi;

    public GenshinCharacterApplicationService(
        ICardService<GenshinCharacterInformation> cardService,
        ICharacterCacheService characterCacheService,
        ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, CharacterApiContext> characterApi,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        ILogger<GenshinCharacterApplicationService> logger)
        : base(gameRoleApi, logger)
    {
        m_CardService = cardService;
        m_CharacterCacheService = characterCacheService;
        m_CharacterApi = characterApi;
    }

    public override async Task<CommandResult> ExecuteAsync(GenshinCharacterApplicationContext context)
    {
        try
        {
            Logger.LogInformation("Executing character service for user {UserId}", context.UserId);

            var region = context.Server.ToRegion();
            var characterName = context.GetParameter<string>("character");

            if (string.IsNullOrWhiteSpace(characterName))
            {
                Logger.LogInformation("Character name is empty for user {UserId}", context.UserId);
                return CommandResult.Failure("Character name cannot be empty. Please provide a valid character name.");
            }

            var profile = await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.Genshin, region);

            if (profile == null)
            {
                return CommandResult.Failure("Invalid HoYoLAB UID or Cookies. Please authenticate again");
            }

            var gameUid = profile.GameUid;

            var charListResponse = await
                m_CharacterApi.GetAllCharactersAsync(new(context.UserId, context.LtUid, context.LToken, gameUid, region));
            if (!charListResponse.IsSuccess)
            {
                Logger.LogWarning(
                    "Failed to fetch character list for gameUid: {GameUid}, region: {Region}, error: {Error}",
                    profile.GameUid, context.Server, charListResponse.ErrorMessage);
                return CommandResult.Failure("Failed to fetch character list. Please try again later.");
            }

            var characters = charListResponse.Data;

            var character =
                characters.FirstOrDefault(x => x.Name.Equals(characterName, StringComparison.OrdinalIgnoreCase));
            if (character == null)
            {
                m_CharacterCacheService.GetAliases(Game.Genshin).TryGetValue(characterName, out var name);

                if (name == null ||
                    (character =
                        characters.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) ==
                    null)
                {
                    Logger.LogWarning("Character {CharacterName} not found for user {UserId}", characterName,
                        context.UserId);
                    return CommandResult.Failure($"Character {characterName} not found. Please try again");
                }
            }

            var characterInfo = await m_CharacterApi.GetCharacterDetailAsync(
                new(context.UserId, context.LtUid, context.LToken, gameUid, region, character.Id!.Value));

            if (!characterInfo.IsSuccess)
            {
                Logger.LogInformation("Failed to fetch character detail for gameUid: {GameUid}, characterId: {CharacterId}, error: {Error}",
                    profile.GameUid, character.Id, characterInfo.ErrorMessage);
                return CommandResult.Failure("Failed to fetch character detail. Please try again later.");
            }

            var card = await m_CardService.GetCardAsync(new BaseCardGenerationContext<GenshinCharacterInformation>(context.UserId,
                characterInfo.Data.List[0], context.Server, profile));

            return CommandResult.Success(content: $"<@{context.UserId}>", attachments: [("character_card.jpg", card)]);
        }
        catch (CommandException e)
        {
            Logger.LogError(e, "An error occurred while executing");
            return CommandResult.Failure(e.Message);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "An error occurred while executing");
            return CommandResult.Failure(e.Message);
        }
    }
}
