#region

using Mehrak.Application.Services.Abstractions;
using Mehrak.Application.Services.Common;
using Mehrak.Application.Utility;
using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.Domain.Shared.Utility;
using Mehrak.Domain.User.Models;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Hsr.Types;
using Mehrak.Infrastructure.Context;

#endregion

namespace Mehrak.Application.Services.Hsr.RealTimeNotes;

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

        var profileResult = await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.HonkaiStarRail,
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

        await UpdateGameUidAsync(context.UserId, context.LtUid, Game.HonkaiStarRail, profile.GameUid, server.ToString(), cancellationToken);

        var gameUid = profile.GameUid;

        var notesResult = await m_ApiService.GetAsync(
            new BaseHoYoApiContext(context.UserId, context.LtUid, context.LToken, gameUid, region), cancellationToken);

        if (!notesResult.IsSuccess)
        {
            if (notesResult.StatusCode == StatusCode.Cancelled)
                throw new OperationCanceledException(notesResult.ErrorMessage ?? "Cancelled");
            if (notesResult.StatusCode == StatusCode.Timeout)
                return CommandResult.Failure(CommandFailureReason.Timeout, ResponseMessage.TimeoutError);
            Logger.LogError(LogMessage.ApiError, "Notes", context.UserId, gameUid, notesResult);
            return CommandResult.Failure(CommandFailureReason.ApiError,
                string.Format(ResponseMessage.ApiError, "Real-Time Notes"));
        }

        var notesData = notesResult.Data;

        return await BuildRealTimeNotes(notesData, server, gameUid);
    }

    private async Task<CommandResult> BuildRealTimeNotes(HsrRealTimeNotesData data,
        Server server, string uid)
    {
        var weeklyReset = server.GetNextWeeklyResetUnix();
        var nextWeeklyReset = server.GetNextNextWeeklyResetUnix();

        var tzi = server.GetTimeZoneInfo();

        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tzi);
        var firstCwReset = new DateTime(2025, 11, 10, 4, 0, 0, DateTimeKind.Unspecified);

        var isCwReset = (int)((nowLocal - firstCwReset).TotalDays / 7) % 2 == 1;

        List<ICommandResultComponent> components =
        [
            new CommandText($"Honkai Star Rail Real-Time Notes (UID: {uid})", CommandText.TextType.Header2),
            new CommandSection([
                    new CommandText("Trailblaze Power", CommandText.TextType.Header3),
                    new CommandText($"{data.CurrentStamina}/{data.MaxStamina}"),
                    new CommandText(data.CurrentStamina == data.MaxStamina
                        ? "Already Full!"
                        : $"Recovers <t:{data.StaminaFullTs}:R>", CommandText.TextType.Footer)
                ],
                new CommandAttachment("hsr_tbp.png", AttachmentSourceType.ImageStorage, "hsr/tbp.png")
            ),
            new CommandSection([
                    new CommandText("Echoes of War", CommandText.TextType.Header3),
                    new CommandText(data.WeeklyCocoonCnt > 0
                        ? $"Claimed {data.WeeklyCocoonLimit - data.WeeklyCocoonCnt}/{data.WeeklyCocoonLimit}"
                        : "Fully Claimed!"),
                    new CommandText($"Resets <t:{weeklyReset}:R>", CommandText.TextType.Footer)
                ],
                new CommandAttachment("hsr_weekly.png", AttachmentSourceType.ImageStorage, "hsr/weekly.png")
            ),
            new CommandSection([
                    new CommandText("Simulated Universe", CommandText.TextType.Header3),
                    new CommandText($"{data.CurrentRogueScore}/{data.MaxRogueScore}"),
                    new CommandText(isCwReset ? $"Resets <t:{nextWeeklyReset}:R>" : $"Resets <t:{weeklyReset}:R>", CommandText.TextType.Footer)
                ],
                new CommandAttachment("hsr_rogue.png", AttachmentSourceType.ImageStorage, "hsr/rogue.png")
            ),
            new CommandSection([
                    new CommandText("Currency Wars", CommandText.TextType.Header3),
                    new CommandText($"{data.GridFightWeeklyCur}/{data.GridFightWeeklyMax}"),
                    new CommandText(isCwReset ? $"Resets <t:{weeklyReset}:R>" : $"Resets <t:{nextWeeklyReset}:R>", CommandText.TextType.Footer)
                ],
                new CommandAttachment("hsr_gridfight.png", AttachmentSourceType.ImageStorage, "hsr/gridfight.png")
            )
        ];

        return CommandResult.Success(components, true, true);
    }
}
