#region

using Mehrak.Application.Models.Context;
using Mehrak.Application.Utility;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.Application.Services.Common;

public class DailyCheckInService : IApplicationService<CheckInApplicationContext>
{
    private readonly IUserRepository m_UserRepository;
    private readonly IApiService<IEnumerable<GameRecordDto>, GameRecordApiContext> m_GameRecordApiService;
    private readonly IApiService<CheckInStatus, CheckInApiContext> m_ApiService;
    private readonly ILogger<DailyCheckInService> m_Logger;

    public DailyCheckInService(
        IUserRepository userRepository,
        IApiService<IEnumerable<GameRecordDto>, GameRecordApiContext> gameRecordApiService,
        IApiService<CheckInStatus, CheckInApiContext> apiService,
        ILogger<DailyCheckInService> logger)
    {
        m_UserRepository = userRepository;
        m_GameRecordApiService = gameRecordApiService;
        m_ApiService = apiService;
        m_Logger = logger;
    }

    public async Task<CommandResult> ExecuteAsync(CheckInApplicationContext context)
    {
        try
        {
            var user = await m_UserRepository.GetUserAsync(context.UserId);
            var profile = user?.Profiles?.First(x => x.LtUid == context.LtUid);

            if (user != null && profile != null && profile.LastCheckIn.HasValue)
            {
                var timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
                var nowUtc8 = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZoneInfo);
                var lastCheckInUtc8 = TimeZoneInfo.ConvertTimeFromUtc(profile.LastCheckIn.Value, timeZoneInfo);

                if (lastCheckInUtc8.Date == nowUtc8.Date)
                {
                    m_Logger.LogInformation("User {UserId} has already checked in today for profile {ProfileId}",
                        context.UserId, profile);
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
                await m_UserRepository.CreateOrUpdateUserAsync(user!);
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
