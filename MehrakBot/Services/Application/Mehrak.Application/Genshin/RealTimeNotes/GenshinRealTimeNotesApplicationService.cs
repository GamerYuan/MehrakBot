#region

using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Services;
using Mehrak.Application.Shared.Utility;
using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.Domain.Shared.Utility;
using Mehrak.Domain.User.Models;
using Mehrak.GameApi.GameRole;
using Mehrak.GameApi.Genshin.Types;
using Mehrak.GameApi.Shared.Types;
using Mehrak.Infrastructure.User;

#endregion

namespace Mehrak.Application.Genshin.RealTimeNotes;

internal class GenshinRealTimeNotesApplicationService : BaseApplicationService
{
    private readonly IApiService<GenshinRealTimeNotesData, BaseHoYoApiContext> m_ApiService;

    protected override string CommandName => "Genshin Notes";

    public GenshinRealTimeNotesApplicationService(
        IApiService<GenshinRealTimeNotesData, BaseHoYoApiContext> apiService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        UserDbContext userContext,
        ILogger<GenshinRealTimeNotesApplicationService> logger)
        : base(gameRoleApi, userContext, logger)
    {
        m_ApiService = apiService;
    }

    protected override async Task<CommandResult> ExecuteCommandAsync(IApplicationContext context, CancellationToken cancellationToken = default)
    {
        var server = Enum.Parse<Server>(context.GetParameter("server")!);
        var region = server.ToRegion();

        var (profile, notesResult) = await FetchProfileAndPrimaryAsync(
            context.UserId, context.LtUid, context.LToken, Game.Genshin, region,
            uid => m_ApiService.GetAsync(
                new BaseHoYoApiContext(context.UserId, context.LtUid, context.LToken, uid, region), cancellationToken),
            cancellationToken);

        if (!notesResult.IsSuccess)
        {
            if (notesResult.StatusCode == StatusCode.Cancelled)
                throw new OperationCanceledException(notesResult.ErrorMessage ?? "Cancelled");
            if (notesResult.StatusCode == StatusCode.Timeout)
                return CommandResult.Failure(CommandFailureReason.Timeout, CommandErrorCode.Timeout);
            Logger.LogError(LogMessage.ApiError, "Notes", context.UserId, profile.GameUid, notesResult);
            return CommandResult.Failure(CommandFailureReason.ApiError,
                CommandErrorCode.ApiUnavailable, CommandErrorTarget.RealTimeNotes);
        }

        return CommandResult.Success(BuildOutcome(notesResult.Data, server, profile.GameUid, DateTimeOffset.UtcNow));

    }

    internal static GenshinNotesOutcome BuildOutcome(GenshinRealTimeNotesData data,
        Server server, string uid, DateTimeOffset observedAt)
    {
        observedAt = observedAt.ToUniversalTime();
        var tz = server.GetTimeZoneInfo();
        var nowLocal = TimeZoneInfo.ConvertTime(observedAt, tz);
        var daysUntilMonday = ((int)DayOfWeek.Monday - (int)nowLocal.DayOfWeek + 7) % 7;
        if (daysUntilMonday == 0 && nowLocal.TimeOfDay >= TimeSpan.FromHours(4))
            daysUntilMonday = 7;
        var resetLocal = nowLocal.Date.AddDays(daysUntilMonday).AddHours(4);
        var weeklyReset = new DateTimeOffset(resetLocal, tz.GetUtcOffset(resetLocal)).ToUniversalTime();

        var remaining = data.CurrentExpeditionNum > 0
            ? data.Expeditions!.Max(x => long.Parse(x.RemainedTime!))
            : 0;
        var expeditionState = data.CurrentExpeditionNum <= 0
            ? ExpeditionOutcome.Undispatched
            : remaining > 0 ? ExpeditionOutcome.InProgress : ExpeditionOutcome.Completed;

        return new GenshinNotesOutcome(uid, observedAt,
            new NotesResource(data.CurrentResin, data.MaxResin,
                data.CurrentResin == data.MaxResin ? null : observedAt.AddSeconds(long.Parse(data.ResinRecoveryTime!))),
            new ExpeditionNotes(data.CurrentExpeditionNum, data.MaxExpeditionNum, expeditionState,
                expeditionState == ExpeditionOutcome.InProgress ? observedAt.AddSeconds(remaining) : null),
            new NotesResource(data.CurrentHomeCoin, data.MaxHomeCoin,
                data.CurrentHomeCoin == data.MaxHomeCoin ? null : observedAt.AddSeconds(long.Parse(data.HomeCoinRecoveryTime!))),
            data.RemainResinDiscountNum, data.ResinDiscountNumLimit, weeklyReset,
            data.Transformer?.Obtained == true
                ? new TransformerNotes(data.Transformer.RecoveryTime!.Reached, weeklyReset)
                : null);
    }
}
