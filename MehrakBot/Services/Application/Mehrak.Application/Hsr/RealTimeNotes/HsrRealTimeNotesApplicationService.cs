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
using Mehrak.GameApi.Hsr.Types;
using Mehrak.GameApi.Shared.Types;
using Mehrak.Infrastructure.User;

#endregion

namespace Mehrak.Application.Hsr.RealTimeNotes;

internal class HsrRealTimeNotesApplicationService : BaseApplicationService
{
    private readonly IApiService<HsrRealTimeNotesData, BaseHoYoApiContext> m_ApiService;

    protected override string CommandName => "Notes";

    public HsrRealTimeNotesApplicationService(
        IApiService<HsrRealTimeNotesData, BaseHoYoApiContext> apiService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        UserDbContext userContext,
        ILogger<HsrRealTimeNotesApplicationService> logger)
        : base(gameRoleApi, userContext, logger)
    {
        m_ApiService = apiService;
    }

    protected override async Task<CommandResult> ExecuteCommandAsync(IApplicationContext context, CancellationToken cancellationToken = default)
    {
        var server = Enum.Parse<Server>(context.GetParameter("server")!);
        var region = server.ToRegion();

        var (profile, notesResult) = await FetchProfileAndPrimaryAsync(
            context.UserId, context.LtUid, context.LToken, Game.HonkaiStarRail, region,
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

        return CommandResult.Success(BuildOutcome(notesData, server, gameUid, DateTimeOffset.UtcNow));
    }

    internal static HsrNotesOutcome BuildOutcome(HsrRealTimeNotesData data,
        Server server, string uid, DateTimeOffset observedAt)
    {
        observedAt = observedAt.ToUniversalTime();
        var tzi = server.GetTimeZoneInfo();
        var nowLocal = TimeZoneInfo.ConvertTime(observedAt, tzi);
        var daysUntilMonday = ((int)DayOfWeek.Monday - (int)nowLocal.DayOfWeek + 7) % 7;
        if (daysUntilMonday == 0 && nowLocal.TimeOfDay >= TimeSpan.FromHours(4))
            daysUntilMonday = 7;
        var resetLocal = nowLocal.Date.AddDays(daysUntilMonday).AddHours(4);
        var weeklyReset = new DateTimeOffset(resetLocal, tzi.GetUtcOffset(resetLocal)).ToUniversalTime();
        var nextWeeklyReset = weeklyReset.AddDays(7);
        var firstCwReset = new DateTime(2025, 11, 10, 4, 0, 0, DateTimeKind.Unspecified);
        var isCwReset = (int)((nowLocal.DateTime - firstCwReset).TotalDays / 7) % 2 == 1;

        return new HsrNotesOutcome(uid, observedAt,
            new NotesResource(data.CurrentStamina, data.MaxStamina,
                data.CurrentStamina == data.MaxStamina ? null : DateTimeOffset.FromUnixTimeSeconds(data.StaminaFullTs)),
            data.WeeklyCocoonCnt, data.WeeklyCocoonLimit, weeklyReset,
            new NotesCounter(data.CurrentRogueScore, data.MaxRogueScore, isCwReset ? nextWeeklyReset : weeklyReset),
            new NotesCounter(data.GridFightWeeklyCur, data.GridFightWeeklyMax, isCwReset ? weeklyReset : nextWeeklyReset));
    }
}
