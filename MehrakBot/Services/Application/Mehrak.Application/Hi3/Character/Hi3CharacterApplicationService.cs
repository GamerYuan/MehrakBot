using System.Text.Json;
using Mehrak.Application.Services.Hi3;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Builders;
using Mehrak.Application.Shared.Services;
using Mehrak.Application.Shared.Services.Types;
using Mehrak.Application.Shared.Utility;
using Mehrak.Domain.Card;
using Mehrak.Domain.Character;
using Mehrak.Domain.Character.Models;
using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Image;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.Domain.User.Models;
using Mehrak.GameApi.GameRole;
using Mehrak.GameApi.Hi3.Types;
using Mehrak.GameApi.Shared.Types;
using Mehrak.Infrastructure.User;

namespace Mehrak.Application.Hi3.Character;

internal class Hi3CharacterApplicationService : BaseAttachmentApplicationService
{
    private readonly ICardService<Hi3CharacterDetail> m_CardService;
    private readonly ICharacterApiService<Hi3CharacterDetail, Hi3CharacterDetail, CharacterApiContext> m_CharacterApi;
    private readonly IImageUpdaterService m_ImageUpdaterService;
    private readonly ICharacterCacheService m_CharacterCacheService;
    private readonly IAliasService m_AliasService;
    private readonly IApplicationMetrics m_MetricsService;
    private readonly ICharacterPortraitConfigService m_PortraitConfigService;


    protected override string CommandName => "HI3 Character";
    protected override string CardName => "Character";
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
        ICharacterPortraitConfigService portraitConfigService,
        ILogger<Hi3CharacterApplicationService> logger
    ) : base(gameRoleApi, userContext, attachmentStorageService, logger)
    {
        m_CardService = cardService;
        m_CharacterApi = characterApi;
        m_ImageUpdaterService = imageUpdaterService;
        m_CharacterCacheService = characterCacheService;
        m_AliasService = aliasService;
        m_MetricsService = metricsService;
        m_PortraitConfigService = portraitConfigService;
    }

    protected override async Task<CommandResult> ExecuteCommandAsync(IApplicationContext context, CancellationToken cancellationToken = default)
    {
        var characterName = context.GetParameter("character")!;

        var server = Enum.Parse<Hi3Server>(context.GetParameter("server")!);
        var region = server.ToRegion();

        var profileResult = await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.HonkaiImpact3,
            region, cancellationToken);
        if (!profileResult.IsSuccess)
        {
            if (profileResult.StatusCode == StatusCode.Cancelled)
                throw new OperationCanceledException(profileResult.ErrorMessage ?? "Cancelled");
            if (profileResult.StatusCode == StatusCode.Timeout)
                return CommandResult.Failure(CommandFailureReason.Timeout, ResponseMessage.TimeoutError);
            Logger.LogWarning(LogMessage.InvalidLogin, context.UserId);
            return CommandResult.Failure(CommandFailureReason.AuthError, ResponseMessage.AuthError);
        }
        var profile = profileResult.Data;

        await UpdateGameUidAsync(context.UserId, context.LtUid, Game.HonkaiImpact3, profile.GameUid, server.ToString(), cancellationToken);

        var gameUid = profile.GameUid;

        var charResponse = await m_CharacterApi.GetAllCharactersAsync(
            new CharacterApiContext(context.UserId, context.LtUid, context.LToken, gameUid, region), cancellationToken);

        if (!charResponse.IsSuccess)
        {
            if (charResponse.StatusCode == StatusCode.Cancelled)
                throw new OperationCanceledException(charResponse.ErrorMessage ?? "Cancelled");
            if (charResponse.StatusCode == StatusCode.Timeout)
                return CommandResult.Failure(CommandFailureReason.Timeout, ResponseMessage.TimeoutError);
            Logger.LogError(LogMessage.ApiError, "Character", context.UserId, gameUid, charResponse);
            return CommandResult.Failure(CommandFailureReason.ApiError,
                string.Format(ResponseMessage.ApiError, "Character data"));
        }

        var characterList = charResponse.Data.ToList();
        _ = m_CharacterCacheService.UpsertCharacters(Game.HonkaiImpact3,
            characterList.SelectMany(x => x.Costumes.Select(c =>
                new CharacterUpsertEntry(x.Avatar.Name, c.Id))));

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
            m_MetricsService.TrackCharacterSelection(nameof(Game.HonkaiImpact3),
                characterInfo.Avatar.Name.ToLowerInvariant());
            return CommandResult.Success([
                new CommandText($"<@{context.UserId}>", CommandText.TextType.Header3), new CommandAttachment(fileName)
            ]);
        }

        List<Task<bool>> tasks = [];
        var costumeTasks = characterInfo.Costumes
            .Select(x => m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(), ImageProcessors.None, cancellationToken))
            .ToList();

        tasks.AddRange(characterInfo.Stigmatas.Where(x => x.Id != 0)
            .Select(x => m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(), new ImageProcessorBuilder().Resize(132, 0).Build(), cancellationToken)));
        tasks.Add(m_ImageUpdaterService.UpdateImageAsync(characterInfo.Weapon.ToImageData(), new ImageProcessorBuilder().Resize(132, 0).Build(), cancellationToken));

        await Task.WhenAll(tasks.Concat(costumeTasks));

        var hasNonCostumeFailure = tasks.Any(x => !x.Result);
        var baseCostumeUpdated = costumeTasks.Count == 0 || costumeTasks[^1].Result;

        if (hasNonCostumeFailure || !baseCostumeUpdated)
        {
            Logger.LogError(LogMessage.ImageUpdateError, "Character", context.UserId,
                JsonSerializer.Serialize(characterInfo));
            return CommandResult.Failure(CommandFailureReason.ApiError, ResponseMessage.ImageUpdateError);
        }

        var cardContext = new BaseCardGenerationContext<Hi3CharacterDetail>(context.UserId, characterInfo, profile);
        cardContext.SetParameter("server", server);

        var portraitConfigs = new Dictionary<int, CharacterPortraitConfig>();
        foreach (var costume in characterInfo.Costumes)
        {
            var config = await m_PortraitConfigService.GetConfigAsync(Game.HonkaiImpact3, costume.Id);
            if (config != null)
                portraitConfigs[costume.Id] = config;
        }

        if (portraitConfigs.Count > 0)
            cardContext.SetParameter("portraitConfigs", portraitConfigs);

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
}
