#region

using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Services;
using Mehrak.Application.Shared.Services.Types;
using Mehrak.Application.Shared.Utility;
using Mehrak.Domain.Card;
using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Image;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.Domain.User.Models;
using Mehrak.GameApi.GameRole;
using Mehrak.GameApi.Genshin.Types;
using Mehrak.GameApi.Shared.Types;
using Mehrak.Infrastructure.User;

#endregion

namespace Mehrak.Application.Genshin.Arcana;

internal sealed record GenshinArcanaRenderState(
    int TotalNum,
    int CurrNum,
    GenshinArcanaResolvedCard[] Cards);

public class GenshinArcanaApplicationService : BaseAttachmentApplicationService
{
    internal const string RendererVersion = "v7";

    private readonly ICardService<GenshinTarotCardState> m_CardService;
    private readonly IApiService<GenshinTheaterResponseData, BaseHoYoApiContext> m_ApiService;

    protected override string CommandName => "Arcana";
    protected override string CardName => "Lunar Arcana";

    public GenshinArcanaApplicationService(
        ICardService<GenshinTarotCardState> cardService,
        IApiService<GenshinTheaterResponseData, BaseHoYoApiContext> apiService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        UserDbContext userContext,
        IAttachmentStorageService attachmentStorageService,
        ILogger<GenshinArcanaApplicationService> logger)
        : base(gameRoleApi, userContext, attachmentStorageService, logger)
    {
        m_CardService = cardService;
        m_ApiService = apiService;
    }

    protected override async Task<CommandResult> ExecuteCommandAsync(IApplicationContext context,
        CancellationToken cancellationToken = default)
    {
        var server = Enum.Parse<Server>(context.GetParameter("server")!);
        var region = server.ToRegion();

        var (profile, response) = await FetchProfileAndPrimaryAsync(
            context.UserId, context.LtUid, context.LToken, Game.Genshin, region,
            uid => m_ApiService.GetAsync(
                new BaseHoYoApiContext(context.UserId, context.LtUid, context.LToken, uid, region), cancellationToken),
            cancellationToken);

        var gameUid = profile.GameUid;
        if (!response.IsSuccess)
        {
            if (response.StatusCode == StatusCode.Cancelled)
                throw new OperationCanceledException(response.ErrorMessage ?? "Cancelled");
            if (response.StatusCode == StatusCode.Timeout)
                return CommandResult.Failure(CommandFailureReason.Timeout, CommandErrorCode.Timeout);
            if (response.StatusCode == StatusCode.Unauthorized)
            {
                Logger.LogInformation("Lunar Arcana is not unlocked for User {UserId} UID {GameUid}",
                    context.UserId, gameUid);
                return CommandResult.Success(
                    EndgameOutcome.Locked(Game.Genshin, profile.GameUid, EndgameMode.Arcana));
            }

            Logger.LogError(LogMessage.ApiError, "Arcana", context.UserId, gameUid, response);
            return CommandResult.Failure(CommandFailureReason.ApiError,
                CommandErrorCode.ApiUnavailable, CommandErrorTarget.Arcana);
        }

        var tarotState = response.Data.TarotCardState;
        if (tarotState?.List is null || tarotState.List.Count == 0)
        {
            Logger.LogInformation(LogMessage.NoClearRecords, "Arcana", context.UserId, gameUid);
            return CommandResult.Success(EndgameOutcome.Empty(Game.Genshin, profile.GameUid, EndgameMode.Arcana));
        }

        var resolvedCards = GenshinArcanaCardCatalog.Resolve(tarotState);
        var renderState = new GenshinArcanaRenderState(
            tarotState.TotalNum,
            tarotState.CurrNum,
            resolvedCards.ToArray());

        var filename = GetCardFileName("genshin", "arcana", RendererVersion, renderState, profile,
            new { Server = server });
        if (await AttachmentExistsAsync(filename, cancellationToken))
        {
            return CommandResult.Success(EndgameOutcome.Card(Game.Genshin, profile.GameUid, EndgameMode.Arcana,
                StorageReference.Attachment(filename)));
        }

        var cardContext = new BaseCardGenerationContext<GenshinTarotCardState>(
            context.UserId, tarotState, profile);

        using var card = await m_CardService.GetCardAsync(cardContext, cancellationToken);
        if (!await StoreAttachmentAsync(context.UserId, filename, card, cancellationToken))
        {
            Logger.LogError(LogMessage.AttachmentStoreError, filename, context.UserId);
            return CommandResult.Failure(CommandFailureReason.BotError,
                CommandErrorCode.AttachmentStoreFailed, CommandErrorTarget.Attachment);
        }

        return CommandResult.Success(EndgameOutcome.Card(Game.Genshin, profile.GameUid, EndgameMode.Arcana,
            StorageReference.Attachment(filename)));
    }
}
