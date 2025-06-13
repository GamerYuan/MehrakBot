#region

using MehrakCore.Models;
using MehrakCore.Modules.Common;
using MehrakCore.Repositories;
using MehrakCore.Services.Common;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using NetCord.Services;

#endregion

namespace MehrakCore.Services.Commands.Executor;

/// <summary>
/// Base class for command executors that provides common functionality for user validation,
/// authentication, and region management while preserving interface-based dependency injection.
/// </summary>
/// <typeparam name="TLogger">The logger type for the concrete executor</typeparam>
public abstract class BaseCommandExecutor<TLogger> : ICommandExecutor, IAuthenticationListener
{
    protected readonly UserRepository UserRepository;
    protected readonly ILogger<TLogger> Logger;

    private readonly TokenCacheService m_TokenCacheService;
    private readonly IAuthenticationMiddlewareService m_AuthenticationMiddleware;

    public IInteractionContext Context { get; set; } = null!;

    protected BaseCommandExecutor(
        UserRepository userRepository,
        TokenCacheService tokenCacheService,
        IAuthenticationMiddlewareService authenticationMiddleware,
        ILogger<TLogger> logger)
    {
        UserRepository = userRepository;
        m_TokenCacheService = tokenCacheService;
        m_AuthenticationMiddleware = authenticationMiddleware;
        Logger = logger;
    }

    /// <summary>
    /// Validates that the user exists and has the specified profile.
    /// </summary>
    protected async Task<(UserModel? user, UserProfile? profile)> ValidateUserAndProfileAsync(uint profileId)
    {
        var user = await UserRepository.GetUserAsync(Context.Interaction.User.Id);
        if (user?.Profiles == null || user.Profiles.All(x => x.ProfileId != profileId))
        {
            Logger.LogInformation("User {UserId} does not have a profile with ID {ProfileId}",
                Context.Interaction.User.Id, profileId);
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                new InteractionMessageProperties().WithContent("You do not have a profile with this ID")
                    .WithFlags(MessageFlags.Ephemeral)));
            return (null, null);
        }

        var selectedProfile = user.Profiles.First(x => x.ProfileId == profileId);
        return (user, selectedProfile);
    }

    /// <summary>
    /// Gets the cached server for the specified game from the user's profile, or null if not found.
    /// </summary>
    protected Regions? GetCachedServer(UserProfile profile, GameName gameName)
    {
        if (profile.LastUsedRegions != null &&
            profile.LastUsedRegions.TryGetValue(gameName, out var cachedServer))
            return cachedServer;
        return null;
    }

    /// <summary>
    /// Validates that a server is specified, sending an error response if not.
    /// </summary>
    protected async Task<bool> ValidateServerAsync(Regions? server)
    {
        if (server == null)
        {
            Logger.LogInformation("User {UserId} does not have a server selected", Context.Interaction.User.Id);
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                new InteractionMessageProperties().WithContent("No cached server found. Please select a server")
                    .WithFlags(MessageFlags.Ephemeral)));
            return false;
        }

        return true;
    }

    /// <summary>
    /// Attempts to get the authentication token for the user. If not found, initiates authentication flow.
    /// Returns the token if available, or null if authentication is required.
    /// </summary>
    protected async Task<string?> GetOrRequestAuthenticationAsync(UserProfile profile, uint profileId,
        Action storePendingParameters)
    {
        var ltoken = await m_TokenCacheService.GetCacheEntry(Context.Interaction.User.Id, profile.LtUid);
        if (ltoken == null)
        {
            Logger.LogInformation("User {UserId} is not authenticated, registering with middleware",
                Context.Interaction.User.Id);

            // Store pending command parameters using the provided action
            storePendingParameters();

            // Register with authentication middleware
            var guid = m_AuthenticationMiddleware.RegisterAuthenticationListener(Context.Interaction.User.Id, this);

            // Send authentication modal
            await Context.Interaction.SendResponseAsync(
                InteractionCallback.Modal(AuthModalModule.AuthModal(guid, profileId)));

            return null;
        }

        Logger.LogInformation("User {UserId} is already authenticated", Context.Interaction.User.Id);
        return ltoken;
    }

    /// <summary>
    /// Updates the user's game UID and last used region for the specified game.
    /// </summary>
    protected void UpdateUserGameData(UserProfile profile, GameName gameName, Regions server, string gameUid)
    {
        // Update game UIDs
        profile.GameUids ??= new Dictionary<GameName, Dictionary<string, string>>();
        if (!profile.GameUids.ContainsKey(gameName))
            profile.GameUids[gameName] = new Dictionary<string, string>();

        if (!profile.GameUids[gameName].TryAdd(server.ToString(), gameUid))
            profile.GameUids[gameName][server.ToString()] = gameUid;

        // Update last used regions
        profile.LastUsedRegions ??= new Dictionary<GameName, Regions>();

        if (!profile.LastUsedRegions.TryAdd(gameName, server))
            profile.LastUsedRegions[gameName] = server;
    }

    /// <summary>
    /// Sends an error response for authentication failures.
    /// </summary>
    protected async Task SendAuthenticationErrorAsync(string errorMessage)
    {
        await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
            .AddComponents(new TextDisplayProperties($"Authentication failed: {errorMessage}"))
            .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2));
    }

    /// <summary>
    /// Sends an error response for general errors.
    /// </summary>
    protected async Task SendGenericErrorAsync(string message = "An unknown error occurred, please try again later.")
    {
        await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
            .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
            .AddComponents(new TextDisplayProperties(message)));
    }

    public abstract ValueTask ExecuteAsync(params object?[] parameters);

    public abstract Task OnAuthenticationCompletedAsync(AuthenticationResult result);
}
