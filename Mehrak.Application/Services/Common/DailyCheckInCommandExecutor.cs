#region

using Mehrak.Domain.Interfaces;
using MehrakCore.Models;
using MehrakCore.Services.Commands.Executor;
using MehrakCore.Services.Common;
using MehrakCore.Services.Metrics;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;

#endregion

namespace Mehrak.Application.Services.Common;

public class DailyCheckInCommandExecutor : BaseCommandExecutor<DailyCheckInCommandExecutor>,
    IDailyCheckInCommandExecutor
{
    private readonly IDailyCheckInService m_DailyCheckInService;

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
    /// <param name="parameters">
    /// The list of parameters, must be of length 1 (profile ID)
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when parameters count is incorrect
    /// </exception>
    public override async ValueTask ExecuteAsync(params object?[] parameters)
    {
        if (parameters.Length != 1)
            throw new ArgumentException("Invalid parameters count for daily check-in command");

        uint profile = parameters[0] == null ? 1u : (uint)parameters[0]!;
        try
        {
            Logger.LogInformation("User {UserId} used the daily check-in command", Context.Interaction.User.Id);

            (UserModel? user, UserProfile? selectedProfile) = await ValidateUserAndProfileAsync(profile);
            if (user == null || selectedProfile == null)
                return;

            if (selectedProfile.LastCheckIn.HasValue)
            {
                TimeZoneInfo timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
                DateTime nowUtc8 = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZoneInfo);
                DateTime lastCheckInUtc8 = TimeZoneInfo.ConvertTimeFromUtc(selectedProfile.LastCheckIn.Value, timeZoneInfo);

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

            string? ltoken = await GetOrRequestAuthenticationAsync(selectedProfile, profile);

            if (ltoken != null)
            {
                Logger.LogInformation("User {UserId} is already authenticated", Context.Interaction.User.Id);
                await Context.Interaction.SendResponseAsync(
                    InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
                await CheckInAsync(selectedProfile.LtUid, ltoken, profile);
            }
        }
        catch (CommandException e)
        {
            Logger.LogError(e, "Error processing daily check-in command for user {UserId}",
                Context.Interaction.User.Id);
            await SendErrorMessageAsync(e.Message);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error processing daily check-in command for user {UserId}",
                Context.Interaction.User.Id);
            await SendErrorMessageAsync();
        }
    }

    /// <summary>
    /// Handles authentication completion from the middleware
    /// </summary>
    /// <param name="result">The authentication result</param>
    public override async Task OnAuthenticationCompletedAsync(AuthenticationResult result)
    {
        if (!result.IsSuccess)
        {
            Logger.LogWarning("Authentication failed for user {UserId}: {ErrorMessage}",
                result.UserId, result.ErrorMessage);
            return;
        }

        Context = result.Context;

        Logger.LogInformation("Authentication completed successfully for user {UserId}", result.UserId);
        await CheckInAsync(result.LtUid, result.LToken, m_PendingProfile!.Value);
    }

    private async Task CheckInAsync(ulong ltuid, string ltoken, uint profile)
    {
        try
        {
            ApiResult<(bool, string)> response = await m_DailyCheckInService.CheckInAsync(ltuid, ltoken);
            if (!response.IsSuccess)
            {
                Logger.LogError("Daily check-in failed for user {UserId}: {ErrorMessage}",
                    Context.Interaction.User.Id, response.ErrorMessage);
                await SendErrorMessageAsync(response.ErrorMessage);
                BotMetrics.TrackCommand(Context.Interaction.User, "checkin", false);
                return;
            }

            if (response.Data.Item1)
            {
                UserModel? user = await UserRepository.GetUserAsync(Context.Interaction.User.Id);
                if (user != null && user.Profiles != null)
                {
                    UserProfile? selectedProfile = user.Profiles.FirstOrDefault(x => x.ProfileId == profile);
                    if (selectedProfile != null)
                    {
                        selectedProfile.LastCheckIn = DateTime.UtcNow;
                        await UserRepository.CreateOrUpdateUserAsync(user);
                    }
                }
            }

            await Context.Interaction.SendFollowupMessageAsync(
                new InteractionMessageProperties().AddComponents(new TextDisplayProperties(response.Data.Item2))
                    .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2));
            BotMetrics.TrackCommand(Context.Interaction.User, "checkin", true);
        }
        catch (CommandException e)
        {
            Logger.LogError(e, "Error processing daily check-in command for user {UserId}",
                Context.Interaction.User.Id);
            await SendErrorMessageAsync(e.Message);
            BotMetrics.TrackCommand(Context.Interaction.User, "checkin", false);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error processing daily check-in command for user {UserId}",
                Context.Interaction.User.Id);
            await SendErrorMessageAsync();
            BotMetrics.TrackCommand(Context.Interaction.User, "checkin", false);
        }
    }
}
