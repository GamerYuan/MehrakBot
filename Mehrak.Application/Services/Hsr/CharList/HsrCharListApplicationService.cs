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
using System.Text.Json;

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
                Logger.LogWarning(LogMessage.InvalidLogin, context.UserId);
                return CommandResult.Failure(CommandFailureReason.AuthError, ResponseMessage.AuthError);
            }

            var gameUid = profile.GameUid;

            var charResponse = await
                m_CharacterApi.GetAllCharactersAsync(new(context.UserId, context.LtUid, context.LToken, gameUid, region));

            if (!charResponse.IsSuccess)
            {
                Logger.LogError(LogMessage.ApiError, "CharList", context.UserId, gameUid, charResponse.ErrorMessage);
                return CommandResult.Failure(CommandFailureReason.ApiError, string.Format(ResponseMessage.ApiError, "Character List"));
            }

            var characterList = charResponse.Data.FirstOrDefault()?.AvatarList ?? [];

            IEnumerable<Task<bool>> avatarTask = characterList.Select(x => m_ImageUpdaterService
                .UpdateImageAsync(x.ToAvatarImageData(), ImageProcessors.AvatarProcessor));
            IEnumerable<Task<bool>> weaponTask =
                characterList.Where(x => x.Equip is not null).Select(x =>
                    m_ImageUpdaterService.UpdateImageAsync(x.Equip!.ToImageData(),
                        new ImageProcessorBuilder().Resize(150, 0).Build()));

            var completed = await Task.WhenAll(avatarTask.Concat(weaponTask));

            if (completed.Any(x => !x))
            {
                Logger.LogError(LogMessage.ImageUpdateError, "CharList", context.UserId, JsonSerializer.Serialize(characterList));
                return CommandResult.Failure(CommandFailureReason.ApiError, ResponseMessage.ImageUpdateError);
            }

            var card = await m_CardService.GetCardAsync(new
                BaseCardGenerationContext<IEnumerable<HsrCharacterInformation>>(
                context.UserId, characterList, context.Server, profile));

            return CommandResult.Success([new CommandText($"<@{context.UserId}>"), new CommandAttachment("charlist_card.jpg", card)]);
        }
        catch (CommandException e)
        {
            Logger.LogError(e, LogMessage.UnknownError, "CharList", context.UserId, e.Message);
            return CommandResult.Failure(CommandFailureReason.BotError, string.Format(ResponseMessage.CardGenError, "Character List"));
        }
        catch (Exception e)
        {
            Logger.LogError(e, LogMessage.UnknownError, "CharList", context.UserId, e.Message);
            return CommandResult.Failure(CommandFailureReason.Unknown, ResponseMessage.UnknownError);
        }
    }
}
