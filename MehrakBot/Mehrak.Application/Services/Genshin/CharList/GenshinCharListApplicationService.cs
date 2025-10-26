﻿#region

using System.Text.Json;
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

#endregion

namespace Mehrak.Application.Services.Genshin.CharList;

public class GenshinCharListApplicationService : BaseApplicationService<GenshinCharListApplicationContext>
{
    private readonly IImageUpdaterService m_ImageUpdaterService;
    private readonly ICardService<IEnumerable<GenshinBasicCharacterData>> m_CardService;

    private readonly ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, CharacterApiContext>
        m_CharacterApi;

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
            string region = context.Server.ToRegion();

            var profile =
                await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.Genshin, region);

            if (profile == null)
            {
                Logger.LogWarning(LogMessage.InvalidLogin, context.UserId);
                return CommandResult.Failure(CommandFailureReason.AuthError, ResponseMessage.AuthError);
            }

            string gameUid = profile.GameUid;

            var charResponse = await
                m_CharacterApi.GetAllCharactersAsync(new CharacterApiContext(context.UserId, context.LtUid,
                    context.LToken, gameUid, region));

            if (!charResponse.IsSuccess)
            {
                Logger.LogError(LogMessage.ApiError, "CharList", context.UserId, gameUid, charResponse);
                return CommandResult.Failure(CommandFailureReason.ApiError,
                    string.Format(ResponseMessage.ApiError, "Character List"));
            }

            var characterList = charResponse.Data.ToList();

            IEnumerable<Task<bool>> avatarTask =
                characterList.Select(x =>
                    m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(), ImageProcessors.AvatarProcessor));
            IEnumerable<Task<bool>> weaponTask =
                characterList.Select(x =>
                    m_ImageUpdaterService.UpdateImageAsync(x.Weapon.ToImageData(),
                        new ImageProcessorBuilder().Resize(200, 0).Build()));

            var completed = await Task.WhenAll(avatarTask.Concat(weaponTask));
            if (completed.Any(x => !x))
            {
                Logger.LogError(LogMessage.ImageUpdateError, "CharList", context.UserId,
                    JsonSerializer.Serialize(characterList));
                return CommandResult.Failure(CommandFailureReason.ApiError, ResponseMessage.ImageUpdateError);
            }

            var card = await m_CardService.GetCardAsync(
                new BaseCardGenerationContext<IEnumerable<GenshinBasicCharacterData>>(context.UserId,
                    characterList, context.Server, profile));

            return CommandResult.Success([
                new CommandText($"<@{context.UserId}>"), new CommandAttachment("charlist_card.jpg", card)
            ]);
        }
        catch (CommandException e)
        {
            Logger.LogError(e, LogMessage.UnknownError, "CharList", context.UserId, e.Message);
            return CommandResult.Failure(CommandFailureReason.BotError,
                string.Format(ResponseMessage.CardGenError, "Character List"));
        }
        catch (Exception e)
        {
            Logger.LogError(e, LogMessage.UnknownError, "CharList", context.UserId, e.Message);
            return CommandResult.Failure(CommandFailureReason.Unknown, ResponseMessage.UnknownError);
        }
    }
}