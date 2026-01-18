using System.Text.Json;
using Mehrak.Application.Services.Abstractions;
using Mehrak.Application.Services.Common;
using Mehrak.Application.Services.Common.Types;
using Mehrak.Application.Utility;
using Mehrak.Domain.Common;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Hi3.Types;
using Mehrak.Infrastructure.Context;

namespace Mehrak.Application.Services.Hi3.Character;

internal class Hi3CharacterApplicationService : BaseAttachmentApplicationService
{
    private readonly ICardService<Hi3CharacterDetail> m_CardService;
    private readonly ICharacterApiService<Hi3CharacterDetail, Hi3CharacterDetail, CharacterApiContext> m_CharacterApi;
    private readonly IImageUpdaterService m_ImageUpdaterService;
    private readonly ICharacterCacheService m_CharacterCacheService;
    private readonly IAliasService m_AliasService;
    private readonly IApplicationMetrics m_MetricsService;

    public Hi3CharacterApplicationService(
        ICardService<Hi3CharacterDetail> cardService,
        ICharacterApiService<Hi3CharacterDetail, Hi3CharacterDetail, CharacterApiContext> characterApi,
        IImageUpdaterService imageUpdaterService,
        ICharacterCacheService characterCacheService,
        IAliasService aliasService,
        IApplicationMetrics metricsService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        UserDbContext userContext,
        IAttachmentStorageService attachmentStorageService,
        ILogger<Hi3CharacterApplicationService> logger
    ) : base(gameRoleApi, userContext, attachmentStorageService, logger)
    {
        m_CardService = cardService;
        m_CharacterApi = characterApi;
        m_ImageUpdaterService = imageUpdaterService;
        m_CharacterCacheService = characterCacheService;
        m_AliasService = aliasService;
        m_MetricsService = metricsService;
    }

    public override async Task<CommandResult> ExecuteAsync(IApplicationContext context)
    {
        var characterName = context.GetParameter("character")!;

        try
        {
            var server = Enum.Parse<Hi3Server>(context.GetParameter("server")!);
            var region = server.ToRegion();

            var profile = await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.HonkaiImpact3,
                region);

            if (profile == null)
            {
                Logger.LogWarning(LogMessage.InvalidLogin, context.UserId);
                return CommandResult.Failure(CommandFailureReason.AuthError, ResponseMessage.AuthError);
            }

            await UpdateGameUidAsync(context.UserId, context.LtUid, Game.HonkaiImpact3, profile.GameUid, server.ToString());

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
            _ = m_CharacterCacheService.UpsertCharacters(Game.HonkaiImpact3, characterList.Select(x => x.Avatar.Name));

            var characterInfo = characterList.FirstOrDefault(x =>
                x.Avatar.Name.Equals(characterName, StringComparison.OrdinalIgnoreCase));

            if (characterInfo == null)
            {
                m_AliasService.GetAliases(Game.HonkaiImpact3).TryGetValue(characterName, out var alias);
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

            var fileName = GetFileName(JsonSerializer.Serialize(characterInfo), "jpg", profile.GameUid);
            if (await AttachmentExistsAsync(fileName))
            {
                return CommandResult.Success([
                    new CommandText($"<@{context.UserId}>", CommandText.TextType.Header3), new CommandAttachment(fileName)
                ]);
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

            var cardContext = new BaseCardGenerationContext<Hi3CharacterDetail>(context.UserId, characterInfo, profile);
            cardContext.SetParameter("server", server);

            await using var card = await m_CardService.GetCardAsync(cardContext);

            if (!await StoreAttachmentAsync(context.UserId, fileName, card))
            {
                Logger.LogError(LogMessage.AttachmentStoreError, fileName, context.UserId);
                return CommandResult.Failure(CommandFailureReason.BotError, ResponseMessage.AttachmentStoreError);
            }

            m_MetricsService.TrackCharacterSelection(nameof(Game.HonkaiImpact3),
                characterInfo.Avatar.Name.ToLowerInvariant());

            return CommandResult.Success([
                new CommandText($"<@{context.UserId}>", CommandText.TextType.Header3), new CommandAttachment(fileName)
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
