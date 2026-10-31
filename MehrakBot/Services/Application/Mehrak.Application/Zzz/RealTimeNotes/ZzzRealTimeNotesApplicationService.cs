#region

using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Services;
using Mehrak.Application.Shared.Utility;
using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.Domain.User.Models;
using Mehrak.GameApi.GameRole;
using Mehrak.GameApi.Shared.Types;
using Mehrak.GameApi.Zzz.Types;
using Mehrak.Infrastructure.User;

#endregion

namespace Mehrak.Application.Zzz.RealTimeNotes;

internal class ZzzRealTimeNotesApplicationService : BaseApplicationService
{
    private readonly IApiService<ZzzRealTimeNotesData, BaseHoYoApiContext> m_ApiService;

    protected override string CommandName => "Notes";
    public ZzzRealTimeNotesApplicationService(
        IApiService<ZzzRealTimeNotesData, BaseHoYoApiContext> apiService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        UserDbContext userContext,
        ILogger<ZzzRealTimeNotesApplicationService> logger)
        : base(gameRoleApi, userContext, logger)
    {
        m_ApiService = apiService;
    }

    protected override async Task<CommandResult> ExecuteCommandAsync(IApplicationContext context, CancellationToken cancellationToken = default)
    {
        var server = Enum.Parse<Server>(context.GetParameter("server")!);
        var region = server.ToRegion();

        var (profile, notesResult) = await FetchProfileAndPrimaryAsync(
            context.UserId, context.LtUid, context.LToken, Game.ZenlessZoneZero, region,
            uid => m_ApiService.GetAsync(
                new BaseHoYoApiContext(context.UserId, context.LtUid, context.LToken, uid, region), cancellationToken),
            cancellationToken);

        var gameUid = profile.GameUid;

        if (!notesResult.IsSuccess)
        {
            if (notesResult.StatusCode == StatusCode.Cancelled)
                throw new OperationCanceledException(notesResult.ErrorMessage ?? "Cancelled");
            if (notesResult.StatusCode == StatusCode.Timeout)
                return CommandResult.Failure(CommandFailureReason.Timeout, CommandErrorCode.Timeout);
            Logger.LogError(LogMessage.ApiError, "Notes", context.UserId, gameUid, notesResult);
            return CommandResult.Failure(CommandFailureReason.ApiError,
                CommandErrorCode.ApiUnavailable, CommandErrorTarget.RealTimeNotes);
        }

        var notesData = notesResult.Data;

        return CommandResult.Success(BuildOutcome(notesData, gameUid, DateTimeOffset.UtcNow));
    }

    internal static ZzzNotesOutcome BuildOutcome(ZzzRealTimeNotesData data, string uid, DateTimeOffset observedAt)
    {
        observedAt = observedAt.ToUniversalTime();
        return new ZzzNotesOutcome(uid, observedAt,
            new NotesResource(data.Energy.Progress.Current, data.Energy.Progress.Max,
                data.Energy.Restore == 0 ? null : observedAt.AddSeconds(data.Energy.Restore)),
            new NotesCounter(data.Vitality.Current, data.Vitality.Max, null),
            data.CardSign switch
            {
                CardSignState.CardSignNo => ScratchCardOutcome.Incomplete,
                CardSignState.CardSignDone => ScratchCardOutcome.Completed,
                _ => ScratchCardOutcome.Unknown
            },
            data.VhsSale.SaleState switch
            {
                VhsSaleState.SaleStateNo => VideoStoreOutcome.WaitingToOpen,
                VhsSaleState.SaleStateDoing => VideoStoreOutcome.Open,
                VhsSaleState.SaleStateDone => VideoStoreOutcome.RevenueAvailable,
                _ => VideoStoreOutcome.Unknown
            },
            data.BountyCommission.Total == 0 ? null : new NotesCounter(data.BountyCommission.Num,
                data.BountyCommission.Total, observedAt.AddSeconds(data.BountyCommission.RefreshTime)),
            data.WeeklyTask is null ? null : new NotesCounter(data.WeeklyTask.CurPoint,
                data.WeeklyTask.MaxPoint, observedAt.AddSeconds(data.WeeklyTask.RefreshTime)),
            data.TempleManage.CurrencyNextRefreshTs == 0 ? null : new NotesCounter(data.TempleManage.CurrentCurrency,
                data.TempleManage.WeeklyCurrencyMax, DateTimeOffset.FromUnixTimeSeconds(data.TempleManage.CurrencyNextRefreshTs)),
            data.TempleManage.ExpeditionState switch
            {
                ExpeditionState.ExpeditionStateInCanSend => TempleAdventureOutcome.Dispatchable,
                ExpeditionState.ExpeditionStateInProgress => TempleAdventureOutcome.Adventuring,
                ExpeditionState.ExpeditionStateEnd => TempleAdventureOutcome.Completed,
                _ => TempleAdventureOutcome.Unknown
            },
            data.TempleManage.BenchState switch
            {
                BenchState.BenchStateProducing => TempleCraftingOutcome.Crafting,
                BenchState.BenchStateCanProduce => TempleCraftingOutcome.Craftable,
                _ => TempleCraftingOutcome.Unknown
            },
            data.TempleManage.ShelveState switch
            {
                ShelveState.ShelveStateCanSell => TempleSalesOutcome.NotSelling,
                ShelveState.ShelveStateSelling => TempleSalesOutcome.Selling,
                ShelveState.ShelveStateSoldOut => TempleSalesOutcome.OutOfStock,
                _ => TempleSalesOutcome.Unknown
            });
    }
}
