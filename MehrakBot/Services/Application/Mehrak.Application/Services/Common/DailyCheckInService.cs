#region

using Mehrak.Application.Services.Abstractions;
using Mehrak.Application.Utility;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Mehrak.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

#endregion

namespace Mehrak.Application.Services.Common;

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

    public async Task<CommandResult> ExecuteAsync(IApplicationContext context)
    {
        try
        {
            var profile = await m_UserContext.UserProfiles
                .Where(x => x.UserId == (long)context.UserId && x.LtUid == (long)context.LtUid)
                .FirstOrDefaultAsync();

            if (profile != null && profile.LastCheckIn.HasValue)
            {
                var timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
                var nowUtc8 = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZoneInfo);
                var lastCheckInUtc8 = TimeZoneInfo.ConvertTimeFromUtc(profile.LastCheckIn.Value, timeZoneInfo);

                if (lastCheckInUtc8.Date == nowUtc8.Date)
                {
                    m_Logger.LogInformation("User {UserId} has already checked in today for profile {ProfileId}",
                        context.UserId, profile.ProfileId);
                    return CommandResult.Success(
                        [new CommandText("You have already checked in today")],
                        isEphemeral: true);
                }
            }

            m_Logger.LogInformation("Starting daily check-in for user {Uid}", context.UserId);
            var gameRecordResult = await m_GameRecordApiService.GetAsync(new GameRecordApiContext(
                context.UserId, context.LtUid, context.LToken));

            if (!gameRecordResult.IsSuccess || gameRecordResult.Data == null)
            {
                m_Logger.LogWarning(LogMessage.InvalidLogin, context.UserId);
                return CommandResult.Failure(CommandFailureReason.AuthError, ResponseMessage.AuthError);
            }

            var gameRecords = gameRecordResult.Data.ToList();
            if (gameRecords.Count == 0)
            {
                m_Logger.LogWarning("No game records found for user {Uid}", context.UserId);
                return CommandResult.Success([new CommandText("No game records found.")], isEphemeral: true);
            }

            var checkInResults = new List<(bool, string)>();
            foreach (var game in gameRecords.Select(x => x.Game))
            {
                CheckInApiContext apiContext = new(context.UserId, context.LtUid, context.LToken, game);
                var checkInResponse = await m_ApiService.GetAsync(apiContext);

                if (checkInResponse.IsSuccess)
                {
                    switch (checkInResponse.Data)
                    {
                        case CheckInStatus.Success:
                            checkInResults.Add((true, $"{game.ToFriendlyString()}: Check-in successful!"));
                            break;

                        case CheckInStatus.AlreadyCheckedIn:
                            checkInResults.Add((true, $"{game.ToFriendlyString()}: Already checked in today."));
                            break;

                        case CheckInStatus.NoValidProfile:
                            checkInResults.Add((false, $"{game.ToFriendlyString()}: No valid account found."));
                            break;

                        default:
                            checkInResults.Add((false, $"{game.ToFriendlyString()}: Unknown status."));
                            break;
                    }
                }
                else
                {
                    m_Logger.LogError(LogMessage.ApiError, $"Check In {game}", context.UserId, "N/A", checkInResponse);
                    checkInResults.Add((false, $"{game.ToFriendlyString()}: {checkInResponse.ErrorMessage}"));
                }
            }

            var resultContent = string.Join("\n", checkInResults.Select(x => x.Item2));

            if (checkInResults.All(x => x.Item1) && profile != null)
            {
                profile.LastCheckIn = DateTime.UtcNow;

                try
                {
                    await m_UserContext.SaveChangesAsync();
                }
                catch (DbUpdateException e)
                {
                    m_Logger.LogError(e, "Failed to update LastCheckIn for user {UserId}, LtUid {LtUid}", context.UserId, context.LtUid);
                }
            }

            m_Logger.LogInformation("Daily check-in completed for user {Uid}", context.UserId);
            return CommandResult.Success([new CommandText(resultContent)]);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, LogMessage.UnknownError, "Check In", context.UserId, e.Message);
            return CommandResult.Failure(CommandFailureReason.Unknown, ResponseMessage.UnknownError);
        }
    }
}
