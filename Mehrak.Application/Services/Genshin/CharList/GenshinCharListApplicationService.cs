using Mehrak.Application.Builders;
using Mehrak.Application.Services.Common;
using Mehrak.Application.Services.Genshin.Types;
using Mehrak.Application.Utility;
using Mehrak.Domain.Common;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Genshin.Types;
using Microsoft.Extensions.Logging;

namespace Mehrak.Application.Services.Genshin.CharList;

public class GenshinCharListApplicationService : BaseApplicationService<GenshinCharListApplicationContext>
{
    private readonly IImageUpdaterService m_ImageUpdaterService;
    private readonly ICardService<IEnumerable<GenshinBasicCharacterData>> m_CardService;
    private readonly ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, CharacterApiContext> m_CharacterApi;

    public GenshinCharListApplicationService(
        IImageUpdaterService imageUpdaterService,
        ICardService<IEnumerable<GenshinBasicCharacterData>> cardService,
        ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, CharacterApiContext> characterApi,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        ILogger<GenshinCharListApplicationService> logger)
        : base(gameRoleApi, logger)
    {
        m_ImageUpdaterService = imageUpdaterService;
        m_CardService = cardService;
        m_CharacterApi = characterApi;
    }

    public override async Task<CommandResult> ExecuteAsync(GenshinCharListApplicationContext context)
    {
        try
        {
            Logger.LogInformation("Executing character list service for user {UserId}", context.UserId);

            string region = context.Server.ToRegion();

            var profile = await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.Genshin, region);

            if (profile == null)
            {
                Logger.LogWarning("No profile found for user {UserId}", context.UserId);
                return CommandResult.Failure("Invalid HoYoLAB UID or Cookies. Please authenticate again");
            }

            string gameUid = profile.GameUid;

            var charResponse = await
                m_CharacterApi.GetAllCharactersAsync(new(context.UserId, context.LtUid, context.LToken, gameUid, region));

            if (!charResponse.IsSuccess)
            {
                Logger.LogWarning(
                    "Failed to fetch character list for gameUid: {GameUid}, region: {Region}, error: {Error}",
                    gameUid, region, charResponse.ErrorMessage);
                return CommandResult.Failure("Failed to fetch character list. Please try again later.");
            }

            var characterList = charResponse.Data.ToList();

            if (characterList.Count == 0)
            {
                Logger.LogInformation("No characters found for user {UserId}", context.UserId);
                return CommandResult.Failure("No characters found in the account");
            }

            IEnumerable<Task> avatarTask =
                characterList.Select(x =>
                    m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(), ImageProcessors.AvatarProcessor));
            IEnumerable<Task> weaponTask =
                characterList.Select(x =>
                    m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(), new ImageProcessorBuilder().Resize(200, 0).Build()));

            await Task.WhenAll(avatarTask.Concat(weaponTask));

            var card = await m_CardService.GetCardAsync(new BaseCardGenerationContext<IEnumerable<GenshinBasicCharacterData>>(context.UserId,
                characterList, context.Server, profile));

            return CommandResult.Success(content: $"<@{context.UserId}>", attachments: [new("charlist_card.jpg", card)]);
        }
        catch (CommandException e)
        {
            Logger.LogError(e, "Failed to get Character List card for user {UserId}",
                context.UserId);
            return CommandResult.Failure(e.Message);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Failed to get Character List card for user {UserId}",
                context.UserId);
            return CommandResult.Failure("An unknown error occurred while generating character list card");
        }
    }
}
