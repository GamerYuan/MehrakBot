using Mehrak.Application.Builders;
using Mehrak.Application.Services.Common;
using Mehrak.Application.Services.Genshin.Types;
using Mehrak.Application.Utility;
using Mehrak.Domain.Common;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Hsr.Types;
using Microsoft.Extensions.Logging;

namespace Mehrak.Application.Services.Hsr.CharList;

public class HsrCharListApplicationService : BaseApplicationService<HsrCharListApplicationContext>
{
    private readonly ICardService<IEnumerable<HsrCharacterInformation>> m_CardService;
    private readonly IImageUpdaterService m_ImageUpdaterService;
    private readonly ICharacterApiService<HsrBasicCharacterData, HsrCharacterInformation, CharacterApiContext> m_CharacterApi;

    public HsrCharListApplicationService(
        ICardService<IEnumerable<HsrCharacterInformation>> cardService,
        IImageUpdaterService imageUpdaterService,
        ICharacterApiService<HsrBasicCharacterData, HsrCharacterInformation, CharacterApiContext> characterApi,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        ILogger<HsrCharListApplicationService> logger)
        : base(gameRoleApi, logger)
    {
        m_CardService = cardService;
        m_ImageUpdaterService = imageUpdaterService;
        m_CharacterApi = characterApi;
    }

    public override async Task<CommandResult> ExecuteAsync(HsrCharListApplicationContext context)
    {
        try
        {
            string region = context.Server.ToRegion();

            var profile = await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.HonkaiStarRail, region);

            if (profile == null)
            {
                Logger.LogWarning("No profile found for user {UserId}", context.UserId);
                return CommandResult.Failure("Invalid HoYoLAB UID or Cookies. Please authenticate again");
            }

            var gameUid = profile.GameUid;

            var charResponse = await
                m_CharacterApi.GetAllCharactersAsync(new(context.UserId, context.LtUid, context.LToken, gameUid, region));

            if (!charResponse.IsSuccess)
            {
                Logger.LogInformation("No character data found for user {UserId} on {Region} server",
                    context.UserId, region);
                return CommandResult.Failure("Failed to fetch character list. Please try again later.");
            }

            var characterList = charResponse.Data.FirstOrDefault()?.AvatarList ?? [];

            if (characterList.Count == 0)
            {
                Logger.LogInformation("No character data found for user {UserId} on {Region} server",
                    context.UserId, region);
                return CommandResult.Failure("Failed to fetch character list. Please try again later.");
            }

            IEnumerable<Task> avatarTask = characterList.Select(x => m_ImageUpdaterService
                .UpdateImageAsync(x.ToAvatarImageData(), ImageProcessors.AvatarProcessor));
            IEnumerable<Task> weaponTask =
                characterList.Where(x => x.Equip is not null).Select(x =>
                    m_ImageUpdaterService.UpdateImageAsync(x.Equip!.ToImageData(),
                        new ImageProcessorBuilder().Resize(150, 0).Build()));

            await Task.WhenAll(avatarTask.Concat(weaponTask));

            var card = await m_CardService.GetCardAsync(new
                BaseCardGenerationContext<IEnumerable<HsrCharacterInformation>>(
                context.UserId, characterList, context.Server, profile));

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
            return CommandResult.Failure("An error occurred while processing your request");
        }
    }
}
