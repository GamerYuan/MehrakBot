#region

using MehrakCore.Models;
using MehrakCore.Repositories;
using MehrakCore.Services.Commands.Executor;
using MehrakCore.Services.Common;
using MehrakCore.Services.Metrics;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;

#endregion

namespace MehrakCore.Services.Commands.Common;

public class DailyCheckInCommandExecutor : BaseCommandExecutor<DailyCheckInCommandExecutor>,
    IDailyCheckInCommandExecutor
{
    private readonly IDailyCheckInService m_DailyCheckInService;

    private UserModel? m_PendingUser;
    private uint? m_PendingProfile;

    public DailyCheckInCommandExecutor(
        IDailyCheckInService dailyCheckInService,
        UserRepository userRepository,
        TokenCacheService tokenCacheService,
        IAuthenticationMiddlewareService authenticationMiddleware,
        GameRecordApiService gameRecordApiService,
        ILogger<DailyCheckInCommandExecutor> logger)
        : base(userRepository, tokenCacheService, authenticationMiddleware, gameRecordApiService, logger)
    {
        m_DailyCheckInService = dailyCheckInService;
    }

    /// <summary>
    /// Executes the daily check-in command with the provided parameters.
    /// </summary>
    /// <param name="parameters">The list of parameters, must be of length 1 (profile ID)</param>
    /// <exception cref="ArgumentException">Thrown when parameters count is incorrect</exception>
    public override async ValueTask ExecuteAsync(params object?[] parameters)
    {
        if (parameters.Length != 1)
            throw new ArgumentException("Invalid parameters count for daily check-in command");

        var profile = parameters[0] == null ? 1u : (uint)parameters[0]!;
        try
        {
            Logger.LogInformation("User {UserId} used the daily check-in command", Context.Interaction.User.Id);

            var (user, selectedProfile) = await ValidateUserAndProfileAsync(profile);
            if (user == null || selectedProfile == null)
                return;

            if (selectedProfile.LastCheckIn.HasValue)
            {
                var timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
                var nowUtc8 = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZoneInfo);
                var lastCheckInUtc8 = TimeZoneInfo.ConvertTimeFromUtc(selectedProfile.LastCheckIn.Value, timeZoneInfo);

                if (lastCheckInUtc8.Date == nowUtc8.Date)
                {
                    Logger.LogInformation("User {UserId} has already checked in today for profile {ProfileId}",
                        Context.Interaction.User.Id, profile);
                    await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                        new InteractionMessageProperties()
                            .WithContent("You have already checked in today for this profile.")
                            .WithFlags(MessageFlags.Ephemeral)));
                    return;
                }
            }

            m_PendingProfile = profile;
            m_PendingUser = user;

            var ltoken = await GetOrRequestAuthenticationAsync(selectedProfile, profile);

            if (ltoken != null)
            {
                Logger.LogInformation("User {UserId} is already authenticated", Context.Interaction.User.Id);
                await Context.Interaction.SendResponseAsync(
                    InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
                await m_DailyCheckInService.CheckInAsync(Context, user, profile, selectedProfile.LtUid, ltoken);
            }
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error processing daily check-in command for user {UserId}",
                Context.Interaction.User.Id);
            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                .WithComponents([
                    new TextDisplayProperties(
                        "An error occurred while processing your request. Please try again later.")
                ]));
            BotMetrics.TrackCommand(Context.Interaction.User, "checkin", false);
        }
    }

    /// <summary>
    /// Handles authentication completion from the middleware
    /// </summary>
    /// <param name="result">The authentication result</param>
    public override async Task OnAuthenticationCompletedAsync(AuthenticationResult result)
    {
        try
        {
            if (!result.IsSuccess)
            {
                Logger.LogWarning("Authentication failed for user {UserId}: {ErrorMessage}",
                    result.UserId, result.ErrorMessage);
                await SendAuthenticationErrorAsync(result.ErrorMessage);
                return;
            }

            // Update context if available
            if (result.Context != null) Context = result.Context;

            Logger.LogInformation("Authentication completed successfully for user {UserId}", result.UserId);

            // Proceed with the original command using stored parameters
            await m_DailyCheckInService.CheckInAsync(Context, m_PendingUser!, m_PendingProfile!.Value, result.LtUid,
                result.LToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error handling authentication completion for user {UserId}", result.UserId);
            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithContent("An error occurred while processing your authentication")
                .WithFlags(MessageFlags.Ephemeral));
        }
        finally
        {
            // Clear pending parameters
            m_PendingProfile = null;
        }
    }
}
