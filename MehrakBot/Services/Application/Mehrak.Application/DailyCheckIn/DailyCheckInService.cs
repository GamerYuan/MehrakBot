#region

using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Utility;
using Mehrak.Domain.Character.Models;
using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.GameApi.DailyCheckIn;
using Mehrak.GameApi.GameRecord;
using Mehrak.Infrastructure.User;
using Microsoft.EntityFrameworkCore;

#endregion

namespace Mehrak.Application.DailyCheckIn;

public class DailyCheckInService : IApplicationService
{
    private readonly UserDbContext m_UserContext;
    private readonly IApiService<IEnumerable<GameRecordDto>, GameRecordApiContext> m_GameRecordApiService;
    private readonly IApiService<CheckInStatus, CheckInApiContext> m_ApiService;
    private readonly ILogger<DailyCheckInService> m_Logger;

    public DailyCheckInService(
        UserDbContext userContext,
        IApiService<IEnumerable<GameRecordDto>, GameRecordApiContext> gameRecordApiService,
        IApiService<CheckInStatus, CheckInApiContext> apiService,
        ILogger<DailyCheckInService> logger)
    {
        m_UserContext = userContext;
        m_GameRecordApiService = gameRecordApiService;
        m_ApiService = apiService;
        m_Logger = logger;
    }

    public async Task<CommandResult> ExecuteAsync(IApplicationContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var profile = await m_UserContext.UserProfiles
                .Where(x => x.UserId == (long)context.UserId && x.LtUid == (long)context.LtUid)
                .FirstOrDefaultAsync(cancellationToken);

            if (profile != null && profile.LastCheckIn.HasValue)
            {
                var timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
                var nowUtc8 = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZoneInfo);
                var lastCheckInUtc8 = TimeZoneInfo.ConvertTimeFromUtc(profile.LastCheckIn.Value, timeZoneInfo);

                if (lastCheckInUtc8.Date == nowUtc8.Date)
                {
                    m_Logger.LogInformation("User {UserId} has already checked in today for profile {ProfileId}",
                        context.UserId, profile.ProfileId);
                    return CommandResult.Success(DailyCheckInOutcome.AlreadyCompleted());
                }
            }

            m_Logger.LogInformation("Starting daily check-in for user {Uid}", context.UserId);
            var gameRecordResult = await m_GameRecordApiService.GetAsync(new GameRecordApiContext(
                context.UserId, context.LtUid, context.LToken), cancellationToken);

            if (!gameRecordResult.IsSuccess || gameRecordResult.Data == null)
            {
                if (gameRecordResult.StatusCode == StatusCode.Cancelled)
                    throw new OperationCanceledException(gameRecordResult.ErrorMessage ?? "Cancelled");
                if (gameRecordResult.StatusCode == StatusCode.Timeout)
                    return CommandResult.Failure(CommandFailureReason.Timeout, CommandErrorCode.Timeout, CommandErrorTarget.GameProfile);
                m_Logger.LogWarning(LogMessage.InvalidLogin, context.UserId);
                return CommandResult.Failure(CommandFailureReason.AuthError, CommandErrorCode.AuthenticationRequired, CommandErrorTarget.GameProfile);
            }

            var gameRecords = gameRecordResult.Data.ToList();
            if (gameRecords.Count == 0)
            {
                m_Logger.LogWarning("No game records found for user {Uid}", context.UserId);
                return CommandResult.Success(DailyCheckInOutcome.Empty());
            }

            var checkInResults = new List<CheckInItem>();

            var checkInTasks = gameRecords.Select(x =>
            {
                var apiContext = new CheckInApiContext(context.UserId, context.LtUid, context.LToken, x.Game);
                return (x.Game, m_ApiService.GetAsync(apiContext, cancellationToken));
            }).ToList();

            var checkInResponses = await Task.WhenAll(checkInTasks.Select(async task =>
            {
                var (game, responseTask) = task;
                var response = await responseTask;
                return (game, response);
            }));

            var hasTimeout = false;

            foreach (var (game, checkInResponse) in checkInResponses)
            {
                if (checkInResponse.IsSuccess)
                {
                    switch (checkInResponse.Data)
                    {
                        case CheckInStatus.Success:
                            checkInResults.Add(CheckInItem.Completed(game, CheckInOutcome.Succeeded));
                            break;

                        case CheckInStatus.AlreadyCheckedIn:
                            checkInResults.Add(CheckInItem.Completed(game, CheckInOutcome.AlreadyCheckedIn));
                            break;

                        case CheckInStatus.NoValidProfile:
                            checkInResults.Add(CheckInItem.Completed(game, CheckInOutcome.NoValidProfile));
                            break;

                        default:
                            checkInResults.Add(CheckInItem.Failed(game, new(CommandErrorCode.InvalidResponse, CommandErrorTarget.DailyCheckIn)));
                            break;
                    }
                }
                else
                {
                    if (checkInResponse.StatusCode == StatusCode.Cancelled)
                        throw new OperationCanceledException(checkInResponse.ErrorMessage ?? "Cancelled");
                    if (checkInResponse.StatusCode == StatusCode.Timeout)
                    {
                        hasTimeout = true;
                        checkInResults.Add(CheckInItem.Failed(game, new(CommandErrorCode.Timeout, CommandErrorTarget.DailyCheckIn)));
                        continue;
                    }
                    m_Logger.LogError(LogMessage.ApiError, $"Check In {game}", context.UserId, "N/A", checkInResponse);
                    checkInResults.Add(CheckInItem.Failed(game, new(
                        checkInResponse.StatusCode == StatusCode.Unauthorized ? CommandErrorCode.AuthenticationRequired : CommandErrorCode.ApiUnavailable,
                        CommandErrorTarget.DailyCheckIn)));
                }
            }

            var successes = checkInResults.Count(x => x.Outcome is CheckInOutcome.Succeeded or CheckInOutcome.AlreadyCheckedIn);
            var outcome = DailyCheckInOutcome.Create(checkInResults,
                successes == checkInResults.Count ? OperationOutcome.Complete : successes > 0 ? OperationOutcome.Partial : OperationOutcome.Failed);

            if (successes == checkInResults.Count && profile != null)
            {
                profile.LastCheckIn = DateTime.UtcNow;

                try
                {
                    await m_UserContext.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException e)
                {
                    m_Logger.LogError(e, "Failed to update LastCheckIn for user {UserId}, LtUid {LtUid}", context.UserId, context.LtUid);
                }
            }

            m_Logger.LogInformation("Daily check-in completed for user {Uid}", context.UserId);

            if (hasTimeout && successes == 0)
                return CommandResult.Failure(CommandFailureReason.Timeout, CommandErrorCode.Timeout, CommandErrorTarget.DailyCheckIn, outcome);

            return CommandResult.Success(outcome);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return CommandResult.Failure(CommandFailureReason.Cancelled, CommandErrorCode.Cancelled, CommandErrorTarget.None);
        }
        catch (OperationCanceledException)
        {
            return CommandResult.Failure(CommandFailureReason.Timeout, CommandErrorCode.Timeout, CommandErrorTarget.None);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, LogMessage.UnknownError, "Check In", context.UserId, e.Message);
            return CommandResult.Failure(CommandFailureReason.Unknown, CommandErrorCode.Unknown, CommandErrorTarget.None);
        }
    }
}
