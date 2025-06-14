#region

using System.Net;
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
    private readonly GameRecordApiService m_GameRecordApi;

    public IInteractionContext Context { get; set; } = null!;

    protected BaseCommandExecutor(
        UserRepository userRepository,
        TokenCacheService tokenCacheService,
        IAuthenticationMiddlewareService authenticationMiddleware,
        GameRecordApiService gameRecordApi,
        ILogger<TLogger> logger)
    {
        UserRepository = userRepository;
        m_TokenCacheService = tokenCacheService;
        m_AuthenticationMiddleware = authenticationMiddleware;
        m_GameRecordApi = gameRecordApi;
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
    protected async Task<string?> GetOrRequestAuthenticationAsync(UserProfile profile, uint profileId)
    {
        var ltoken = await m_TokenCacheService.GetCacheEntry(Context.Interaction.User.Id, profile.LtUid);
        if (ltoken == null)
        {
            Logger.LogInformation("User {UserId} is not authenticated, registering with middleware",
                Context.Interaction.User.Id);

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
    /// Retrieves the game UID for the specified user and updates their profile with the game UID.
    /// </summary>
    /// <param name="user">The user</param>
    /// <param name="gameName">The game</param>
    /// <param name="ltuid">ltuid</param>
    /// <param name="ltoken">ltoken</param>
    /// <param name="server">server</param>
    /// <param name="region">region string</param>
    /// <returns></returns>
    protected async ValueTask<ApiResult<string>> GetAndUpdateGameUidAsync(UserModel? user, GameName gameName,
        ulong ltuid, string ltoken, Regions server, string region)
    {
        var selectedProfile = user?.Profiles?.FirstOrDefault(x => x.LtUid == ltuid);

        // edge case check that probably will never occur
        // but if user removes their profile while this command is running will result in null
        if (user?.Profiles == null || selectedProfile == null)
        {
            Logger.LogDebug("User {UserId} does not have a profile with ltuid {LtUid}",
                Context.Interaction.User.Id, ltuid);
            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithFlags(MessageFlags.IsComponentsV2).WithComponents([
                    new TextDisplayProperties("No profile found. Please select the correct profile")
                ]));
            return ApiResult<string>.Failure(HttpStatusCode.BadRequest, "No profile found for the specified ltuid");
        }

        if (selectedProfile.GameUids == null ||
            !selectedProfile.GameUids.TryGetValue(gameName, out var dict) ||
            !dict.TryGetValue(server.ToString(), out var gameUid))
        {
            Logger.LogDebug("User {UserId} does not have a game UID for region {Region}",
                Context.Interaction.User.Id, region);
            var result =
                await m_GameRecordApi.GetUserRegionUidAsync(ltuid, ltoken, GetGameIdentifier(gameName), region);
            if (result.RetCode == -100)
            {
                await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                    .WithFlags(MessageFlags.IsComponentsV2).WithComponents([
                        new TextDisplayProperties("Invalid HoYoLAB UID or Cookies. Please authenticate again.")
                    ]));
                return ApiResult<string>.Failure(HttpStatusCode.BadRequest, "Invalid HoYoLAB UID or Cookies");
            }

            gameUid = result.Data;
        }

        if (gameUid == null)
        {
            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithFlags(MessageFlags.IsComponentsV2).WithComponents([
                    new TextDisplayProperties("No game information found. Please select the correct region")
                ]));
            return ApiResult<string>.Failure(HttpStatusCode.BadRequest,
                "No game information found. Please select the correct region");
        }

        // Update game UIDs
        selectedProfile.GameUids ??= new Dictionary<GameName, Dictionary<string, string>>();
        if (!selectedProfile.GameUids.ContainsKey(gameName))
            selectedProfile.GameUids[gameName] = new Dictionary<string, string>();

        if (!selectedProfile.GameUids[gameName].TryAdd(server.ToString(), gameUid))
            selectedProfile.GameUids[gameName][server.ToString()] = gameUid;

        // Update last used regions
        selectedProfile.LastUsedRegions ??= new Dictionary<GameName, Regions>();

        if (!selectedProfile.LastUsedRegions.TryAdd(gameName, server))
            selectedProfile.LastUsedRegions[gameName] = server;

        await UserRepository.CreateOrUpdateUserAsync(user).ConfigureAwait(false);

        return ApiResult<string>.Success(gameUid);
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

    private static string GetGameIdentifier(GameName gameName)
    {
        return gameName switch
        {
            GameName.Genshin => "hk4e_global",
            GameName.HonkaiStarRail => "hkrpg_global",
            GameName.ZenlessZoneZero => "nap_global",
            GameName.HonkaiImpact3 => "expr",
            _ => throw new ArgumentOutOfRangeException(nameof(gameName), gameName, null)
        };
    }
}