#region

using MehrakCore.Modules.Common;
using MehrakCore.Repositories;
using MehrakCore.Services.Common;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using IInteractionContext = NetCord.Services.IInteractionContext;

#endregion

namespace MehrakCore.Services.Commands.Common;

public class DailyCheckInCommandExecutor : IDailyCheckInCommandService,
    IAuthenticationListener
{
    private readonly IDailyCheckInService m_DailyCheckInService;
    private readonly UserRepository m_UserRepository;
    private readonly TokenCacheService m_TokenCacheService;
    private readonly IAuthenticationMiddlewareService m_AuthenticationMiddleware;
    private readonly ILogger<DailyCheckInCommandExecutor> m_Logger;

    // Fields to store pending command parameters during authentication
    private uint? m_PendingProfile;

    public IInteractionContext Context { get; set; } = null!;

    public DailyCheckInCommandExecutor(
        IDailyCheckInService dailyCheckInService,
        UserRepository userRepository,
        TokenCacheService tokenCacheService,
        IAuthenticationMiddlewareService authenticationMiddleware,
        ILogger<DailyCheckInCommandExecutor> logger)
    {
        m_DailyCheckInService = dailyCheckInService;
        m_UserRepository = userRepository;
        m_TokenCacheService = tokenCacheService;
        m_AuthenticationMiddleware = authenticationMiddleware;
        m_Logger = logger;
    }

    /// <summary>
    /// Executes the daily check-in command with the provided parameters.
    /// </summary>
    /// <param name="parameters">The list of parameters, must be of length 1 (profile ID)</param>
    /// <exception cref="ArgumentException">Thrown when parameters count is incorrect</exception>
    public async ValueTask ExecuteAsync(params object?[] parameters)
    {
        if (parameters.Length != 1)
            throw new ArgumentException("Invalid parameters count for daily check-in command");

        var profile = parameters[0] == null ? 1u : (uint)parameters[0]!;

        try
        {
            m_Logger.LogInformation("User {UserId} used the daily check-in command", Context.Interaction.User.Id);

            var user = await m_UserRepository.GetUserAsync(Context.Interaction.User.Id);
            if (user?.Profiles == null || user.Profiles.All(x => x.ProfileId != profile))
            {
                m_Logger.LogInformation("User {UserId} does not have a profile with ID {ProfileId}",
                    Context.Interaction.User.Id, profile);
                await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                    new InteractionMessageProperties().WithContent("You do not have a profile with this ID")
                        .WithFlags(MessageFlags.Ephemeral)));
                return;
            }

            var selectedProfile = user.Profiles.First(x => x.ProfileId == profile);

            var ltoken = await m_TokenCacheService.GetCacheEntry(Context.Interaction.User.Id, selectedProfile.LtUid);
            if (ltoken == null)
            {
                m_Logger.LogInformation("User {UserId} is not authenticated, registering with middleware",
                    Context.Interaction.User.Id);

                // Store pending command parameters
                m_PendingProfile = profile;

                // Register with authentication middleware
                var guid = m_AuthenticationMiddleware.RegisterAuthenticationListener(Context.Interaction.User.Id, this);

                // Send authentication modal
                await Context.Interaction.SendResponseAsync(
                    InteractionCallback.Modal(AuthModalModule.AuthModal(guid, profile)));
            }
            else
            {
                m_Logger.LogInformation("User {UserId} is already authenticated", Context.Interaction.User.Id);
                await Context.Interaction.SendResponseAsync(
                    InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
                await m_DailyCheckInService.CheckInAsync(Context, selectedProfile.LtUid, ltoken);
            }
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Error processing daily check-in command for user {UserId}",
                Context.Interaction.User.Id);
            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                .WithComponents([
                    new TextDisplayProperties(
                        "An error occurred while processing your request. Please try again later.")
                ]));
        }
    }

    /// <summary>
    /// Handles authentication completion from the middleware
    /// </summary>
    /// <param name="result">The authentication result</param>
    public async Task OnAuthenticationCompletedAsync(AuthenticationResult result)
    {
        try
        {
            if (!result.IsSuccess)
            {
                m_Logger.LogWarning("Authentication failed for user {UserId}: {ErrorMessage}",
                    result.UserId, result.ErrorMessage);

                await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                    .WithContent($"Authentication failed: {result.ErrorMessage}")
                    .WithFlags(MessageFlags.Ephemeral));
                return;
            }

            m_Logger.LogInformation("Authentication completed successfully for user {UserId}", result.UserId);

            // Proceed with the original command using stored parameters
            if (m_PendingProfile.HasValue && result.LToken != null)
                await m_DailyCheckInService.CheckInAsync(Context, result.LtUid, result.LToken);
            else
                await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                    .WithContent("Error: Missing required parameters for command execution")
                    .WithFlags(MessageFlags.Ephemeral));
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Error handling authentication completion for user {UserId}", result.UserId);
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