using System.Text.Json;
using Mehrak.Application.Services.Common;
using Mehrak.Application.Services.Hi3.Types;
using Mehrak.Application.Utility;
using Mehrak.Domain.Common;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Hi3.Types;
using Microsoft.Extensions.Logging;

namespace Mehrak.Application.Services.Hi3.Character;

internal class Hi3CharacterApplicationService : BaseApplicationService<Hi3CharacterApplicationContext>
{
    private readonly ICardService<ICardGenerationContext<Hi3CharacterDetail, Hi3Server>, Hi3CharacterDetail, Hi3Server> m_CardService;
    private readonly ICharacterApiService<Hi3CharacterDetail, Hi3CharacterDetail, CharacterApiContext> m_CharacterApi;
    private readonly IImageUpdaterService m_ImageUpdaterService;
    private readonly ICharacterCacheService m_CharacterCacheService;
    private readonly IMetricsService m_MetricsService;

    public Hi3CharacterApplicationService(
        ICardService<ICardGenerationContext<Hi3CharacterDetail, Hi3Server>, Hi3CharacterDetail, Hi3Server> cardService,
        ICharacterApiService<Hi3CharacterDetail, Hi3CharacterDetail, CharacterApiContext> characterApi,
        IImageUpdaterService imageUpdaterService,
        ICharacterCacheService characterCacheService,
        IMetricsService metricsService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        IUserRepository userRepository,
        ILogger<Hi3CharacterApplicationService> logger
    ) : base(gameRoleApi, userRepository, logger)
    {
        m_CardService = cardService;
        m_CharacterApi = characterApi;
        m_ImageUpdaterService = imageUpdaterService;
        m_CharacterCacheService = characterCacheService;
        m_MetricsService = metricsService;
    }

    public override async Task<CommandResult> ExecuteAsync(Hi3CharacterApplicationContext context)
    {
        var characterName = context.GetParameter<string>("character")!;

        try
        {
            var region = context.Server.ToRegion();

            var profile = await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.HonkaiImpact3,
                region);

            if (profile == null)
            {
                Logger.LogWarning(LogMessage.InvalidLogin, context.UserId);
                return CommandResult.Failure(CommandFailureReason.AuthError, ResponseMessage.AuthError);
            }

            await UpdateGameUidAsync(context.UserId, context.LtUid, Game.HonkaiImpact3, profile.GameUid, context.Server.ToString());

            var gameUid = profile.GameUid;

            var charResponse = await m_CharacterApi.GetAllCharactersAsync(
                new CharacterApiContext(context.UserId, context.LtUid, context.LToken, gameUid, region));

            if (!charResponse.IsSuccess)
            {
                Logger.LogError(LogMessage.ApiError, "Character", context.UserId, gameUid, charResponse);
                return CommandResult.Failure(CommandFailureReason.ApiError,
                    string.Format(ResponseMessage.ApiError, "Character data"));
            }

            var characterList = charResponse.Data.ToList();

            var characterInfo = characterList.FirstOrDefault(x =>
                x.Avatar.Name.Equals(characterName, StringComparison.OrdinalIgnoreCase));

            if (characterInfo == null)
            {
                m_CharacterCacheService.GetAliases(Game.HonkaiImpact3).TryGetValue(characterName, out var alias);
                if (alias == null ||
                    (characterInfo = characterList.FirstOrDefault(x =>
                        x.Avatar.Name.Equals(alias, StringComparison.OrdinalIgnoreCase))) == null)
                {
                    Logger.LogWarning(LogMessage.CharNotFoundInfo, characterName, context.UserId, gameUid);
                    return CommandResult.Success([
                        new CommandText(
                            string.Format(ResponseMessage.CharacterNotFound, characterName))
                    ], isEphemeral: true);
                }
            }

            List<Task<bool>> tasks = [];

            tasks.AddRange(characterInfo.Stigmatas.Where(x => x.Id != 0)
                .Select(x => m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(), ImageProcessors.None)));
            tasks.AddRange(characterInfo.Costumes.Select(x => m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(), ImageProcessors.None)));
            tasks.Add(m_ImageUpdaterService.UpdateImageAsync(characterInfo.Weapon.ToImageData(), ImageProcessors.None));

            var completed = await Task.WhenAll(tasks);

            if (completed.Any(x => !x))
            {
                Logger.LogError(LogMessage.ImageUpdateError, "Character", context.UserId,
                    JsonSerializer.Serialize(characterInfo));
                return CommandResult.Failure(CommandFailureReason.ApiError, ResponseMessage.ImageUpdateError);
            }

            var card = await m_CardService.GetCardAsync(
                new Hi3CardGenerationContext<Hi3CharacterDetail>(context.UserId, characterInfo, context.Server,
                    profile));

            m_MetricsService.TrackCharacterSelection(nameof(Game.HonkaiImpact3),
                characterInfo.Avatar.Name.ToLowerInvariant());

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
