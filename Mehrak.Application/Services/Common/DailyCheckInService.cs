#region

using Mehrak.Application.Models.Context;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.Application.Services.Common;

public class DailyCheckInService : IApplicationService<CheckInApplicationContext>
{
    private readonly IApiService<IEnumerable<GameRecordDto>, GameRecordApiContext> m_GameRecordApiService;
    private readonly IApiService<CheckInStatus, CheckInApiContext> m_ApiService;
    private readonly ILogger<DailyCheckInService> m_Logger;

    public DailyCheckInService(IApiService<IEnumerable<GameRecordDto>, GameRecordApiContext> gameRecordApiService,
        IApiService<CheckInStatus, CheckInApiContext> apiService,
        ILogger<DailyCheckInService> logger)
    {
        m_GameRecordApiService = gameRecordApiService;
        m_ApiService = apiService;
        m_Logger = logger;
    }

    public async Task<CommandResult> ExecuteAsync(CheckInApplicationContext context)
    {
        try
        {
            m_Logger.LogInformation("Starting daily check-in for user {Uid}", context.UserId);
            var gameRecordResult = await m_GameRecordApiService.GetAsync(new GameRecordApiContext(
                context.UserId, context.LtUid, context.LToken));
            if (!gameRecordResult.IsSuccess || gameRecordResult.Data == null)
            {
                m_Logger.LogWarning("Failed to retrieve game records for user {Uid}", context.UserId);
                return CommandResult.Failure("Failed to retrieve game records.");
            }
            var gameRecords = gameRecordResult.Data.ToList();
            if (gameRecords.Count == 0)
            {
                m_Logger.LogWarning("No game records found for user {Uid}", context.UserId);
                return CommandResult.Failure("No game records found.");
            }

            var checkInResults = new List<string>();
            foreach (var game in gameRecords.Select(x => x.Game))
            {
                CheckInApiContext apiContext = new(context.UserId, context.LtUid, context.LToken, game);
                var checkInResponse = await m_ApiService.GetAsync(apiContext);

                if (checkInResponse.IsSuccess)
                {
                    switch (checkInResponse.Data)
                    {
                        case CheckInStatus.Success:
                            checkInResults.Add($"{game.ToFriendlyString()}: Check-in successful!");
                            break;

                        case CheckInStatus.AlreadyCheckedIn:
                            checkInResults.Add($"{game.ToFriendlyString()}: Already checked in today.");
                            break;

                        case CheckInStatus.NoValidProfile:
                            checkInResults.Add($"{game.ToFriendlyString()}: No valid account found.");
                            break;

                        default:
                            checkInResults.Add($"{game.ToFriendlyString()}: Unknown status.");
                            break;
                    }
                }
                else
                {
                    checkInResults.Add($"{game.ToFriendlyString()}: {checkInResponse.ErrorMessage}");
                }
            }
            var resultContent = string.Join("\n", checkInResults);
            m_Logger.LogInformation("Daily check-in completed for user {Uid}", context.UserId);
            return CommandResult.Success(content: resultContent);
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "An error occurred during daily check-in for user {Uid}", context.UserId);
            return CommandResult.Failure("An unexpected error occurred.");
        }
    }
}
