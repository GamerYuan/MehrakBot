#region

using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Models;
using MehrakCore.Modules;
using MehrakCore.Modules.Common;
using MehrakCore.Repositories;
using MehrakCore.Services.Commands.Executor;
using MehrakCore.Services.Common;
using MehrakCore.Services.Metrics;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using IInteractionContext = NetCord.Services.IInteractionContext;

#endregion

namespace MehrakCore.Services.Commands.Genshin;

public class GenshinCharacterCommandExecutor : ICharacterCommandExecutor<GenshinCommandModule>, IAuthenticationListener
{
    private readonly ICharacterApi<GenshinBasicCharacterData, GenshinCharacterDetail> m_GenshinCharacterApiService;
    private readonly GameRecordApiService m_GameRecordApiService;
    private readonly ICharacterCardService<GenshinCharacterInformation> m_GenshinCharacterCardService;
    private readonly GenshinImageUpdaterService m_GenshinImageUpdaterService;
    private readonly UserRepository m_UserRepository;
    private readonly ILogger<GenshinCharacterCommandExecutor> m_Logger;
    private readonly TokenCacheService m_TokenCacheService;

    private readonly IAuthenticationMiddlewareService
        m_AuthenticationMiddleware; // Fields to store pending command parameters during authentication

    private string? m_PendingCharacterName;
    private Regions? m_PendingServer;

    public IInteractionContext Context { get; set; } = null!;

    public GenshinCharacterCommandExecutor(
        ICharacterApi<GenshinBasicCharacterData, GenshinCharacterDetail> genshinCharacterApiService,
        GameRecordApiService gameRecordApiService,
        ICharacterCardService<GenshinCharacterInformation> genshinCharacterCardService,
        GenshinImageUpdaterService genshinImageUpdaterService, UserRepository userRepository,
        ILogger<GenshinCharacterCommandExecutor> logger,
        TokenCacheService tokenCacheService,
        IAuthenticationMiddlewareService authenticationMiddleware)
    {
        m_GenshinCharacterApiService = genshinCharacterApiService;
        m_GameRecordApiService = gameRecordApiService;
        m_GenshinCharacterCardService = genshinCharacterCardService;
        m_GenshinImageUpdaterService = genshinImageUpdaterService;
        m_UserRepository = userRepository;
        m_Logger = logger;
        m_TokenCacheService = tokenCacheService;
        m_AuthenticationMiddleware = authenticationMiddleware;
    }

    /// <summary>
    /// Executes the character command with the provided parameters.
    /// </summary>
    /// <param name="parameters">The list of parameters, must be of length 3</param>
    /// <exception cref="ArgumentException">Thrown when parameters count is incorrect</exception>
    public async ValueTask ExecuteAsync(params object?[] parameters)
    {
        if (parameters.Length != 3)
            throw new ArgumentException("Invalid parameters count for character command");

        var characterName = parameters[0] == null ? string.Empty : (string)parameters[0]!;
        var server = (Regions?)parameters[1];
        var profile = parameters[2] == null ? 1 : (uint)parameters[2]!;

        try
        {
            m_Logger.LogInformation("User {UserId} used the character command", Context.Interaction.User.Id);
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
            if (selectedProfile.LastUsedRegions != null && !server.HasValue &&
                selectedProfile.LastUsedRegions.TryGetValue(GameName.Genshin, out var tmp)) server = tmp;

            if (server == null)
            {
                m_Logger.LogInformation("User {UserId} does not have a server selected", Context.Interaction.User.Id);
                await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                    new InteractionMessageProperties().WithContent("No cached server found. Please select a server")
                        .WithFlags(MessageFlags.Ephemeral)));
                return;
            }

            var ltoken = await m_TokenCacheService.GetCacheEntry(Context.Interaction.User.Id, selectedProfile.LtUid);
            if (ltoken == null)
            {
                m_Logger.LogInformation("User {UserId} is not authenticated, registering with middleware",
                    Context.Interaction.User.Id);

                // Store pending command parameters
                m_PendingCharacterName = characterName;
                m_PendingServer = server.Value;

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
                await SendCharacterCardResponseAsync(selectedProfile.LtUid, ltoken, characterName,
                    server.Value);
            }
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Error processing character command for user {UserId}", Context.Interaction.User.Id);
            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                .WithComponents([
                    new TextDisplayProperties(
                        "An error occurred while processing your request. Please try again later.")
                ]));
        }
    }

    public async Task SendCharacterCardResponseAsync(ulong ltuid, string ltoken, string characterName, Regions server)
    {
        try
        {
            var region = GetRegion(server);
            var user = await m_UserRepository.GetUserAsync(Context.Interaction.User.Id);

            var selectedProfile = user?.Profiles?.FirstOrDefault(x => x.LtUid == ltuid);

            // edge case check that probably will never occur
            // but if user removes their profile while this command is running will result in null
            if (user?.Profiles == null || selectedProfile == null)
            {
                m_Logger.LogDebug("User {UserId} does not have a profile with ltuid {LtUid}",
                    Context.Interaction.User.Id, ltuid);
                await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                    .WithFlags(MessageFlags.IsComponentsV2).WithComponents([
                        new TextDisplayProperties("No profile found. Please select the correct profile")
                    ]));
                return;
            }

            if (selectedProfile.GameUids == null ||
                !selectedProfile.GameUids.TryGetValue(GameName.Genshin, out var dict) ||
                !dict.TryGetValue(server.ToString(), out var gameUid))
            {
                m_Logger.LogDebug("User {UserId} does not have a game UID for region {Region}",
                    Context.Interaction.User.Id, region);
                var result = await m_GameRecordApiService.GetUserRegionUidAsync(ltuid, ltoken, "hk4e_global", region);
                if (result.RetCode == -100)
                {
                    await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                        .WithFlags(MessageFlags.IsComponentsV2).WithComponents([
                            new TextDisplayProperties("Invalid HoYoLAB UID or Cookies. Please authenticate again.")
                        ]));
                    return;
                }

                gameUid = result.Data;
            }

            if (gameUid == null)
            {
                await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                    .WithFlags(MessageFlags.IsComponentsV2).WithComponents([
                        new TextDisplayProperties("No game information found. Please select the correct region")
                    ]));
                return;
            }

            selectedProfile.GameUids ??= new Dictionary<GameName, Dictionary<string, string>>();

            if (!selectedProfile.GameUids.ContainsKey(GameName.Genshin))
                selectedProfile.GameUids[GameName.Genshin] = new Dictionary<string, string>();
            if (!selectedProfile.GameUids[GameName.Genshin].TryAdd(server.ToString(), gameUid))
                selectedProfile.GameUids[GameName.Genshin][server.ToString()] = gameUid;

            m_Logger.LogDebug("Found game UID {GameUid} for User {UserId} in region {Region}", gameUid,
                Context.Interaction.User.Id, region);

            selectedProfile.LastUsedRegions ??= new Dictionary<GameName, Regions>();

            if (!selectedProfile.LastUsedRegions.TryAdd(GameName.Genshin, server))
                selectedProfile.LastUsedRegions[GameName.Genshin] = server;

            var updateUser = m_UserRepository.CreateOrUpdateUserAsync(user);

            var characters = (await m_GenshinCharacterApiService.GetAllCharactersAsync(ltuid, ltoken, gameUid, region))
                .ToArray();

            var character =
                characters.FirstOrDefault(x => x.Name.Equals(characterName, StringComparison.OrdinalIgnoreCase));
            if (character == null)
            {
                await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                    .WithFlags(MessageFlags.IsComponentsV2 | MessageFlags.Ephemeral).WithComponents([
                        new TextDisplayProperties("Character not found. Please try again.")
                    ]));
                return;
            }

            var properties =
                await GenerateCharacterCardResponseAsync((uint)character.Id!.Value, ltuid, ltoken, gameUid, region);
            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithFlags(MessageFlags.IsComponentsV2 | MessageFlags.Ephemeral)
                .AddComponents(new TextDisplayProperties("Command execution completed")));
            var followup = Context.Interaction.SendFollowupMessageAsync(properties);
            await Task.WhenAll(followup, updateUser);
            BotMetrics.TrackCommand(Context.Interaction.User, "genshin character", true);
            BotMetrics.TrackCharacterSelection(nameof(GameName.Genshin), characterName);
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Error sending character card response for user {UserId}",
                Context.Interaction.User.Id);
            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithFlags(MessageFlags.IsComponentsV2 | MessageFlags.Ephemeral)
                .WithComponents([
                    new TextDisplayProperties(
                        "An error occurred while processing your request. Please try again later.")
                ]));
            BotMetrics.TrackCommand(Context.Interaction.User, "genshin character", false);
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

            // Update context if available
            if (result.Context != null) Context = result.Context;

            // Check if we have the required pending parameters
            if (string.IsNullOrEmpty(m_PendingCharacterName) || !m_PendingServer.HasValue)
            {
                m_Logger.LogWarning("Missing required parameters for command execution for user {UserId}",
                    result.UserId);
                await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                    .WithContent("Error: Missing required parameters for command execution")
                    .WithFlags(MessageFlags.Ephemeral));
                return;
            }

            m_Logger.LogInformation("Authentication completed successfully for user {UserId}", result.UserId);

            await SendCharacterCardResponseAsync(result.LtUid, result.LToken, m_PendingCharacterName,
                m_PendingServer.Value);
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Error handling authentication completion for user {UserId}", result.UserId);
            if (Context?.Interaction != null)
                await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                    .WithContent("An error occurred while processing your authentication")
                    .WithFlags(MessageFlags.Ephemeral));
        }
    }

    private async Task<InteractionMessageProperties> GenerateCharacterCardResponseAsync(uint characterId, ulong ltuid,
        string ltoken, string gameUid, string region)
    {
        var result =
            await m_GenshinCharacterApiService.GetCharacterDataFromIdAsync(ltuid, ltoken, gameUid, region, characterId);

        if (result.RetCode == 10001)
        {
            m_Logger.LogError("Failed to retrieve character data {CharacterId} for user {UserId}", region,
                Context.Interaction.User.Id);
            return new InteractionMessageProperties().WithComponents([
                new TextDisplayProperties("Invalid HoYoLAB UID or Cookies. Please authenticate again.")
            ]);
        }

        var characterDetail = result.Data;

        if (characterDetail == null || characterDetail.List.Count == 0)
        {
            m_Logger.LogError("Failed to retrieve character data {CharacterId} for user {UserId}",
                region, Context.Interaction.User.Id);
            return new InteractionMessageProperties().WithComponents([
                new TextDisplayProperties("Failed to retrieve character data. Please try again.")
            ]);
        }

        var characterInfo = characterDetail.List[0];

        await m_GenshinImageUpdaterService.UpdateDataAsync(characterInfo, [characterDetail.AvatarWiki]);

        InteractionMessageProperties properties = new();
        properties.WithFlags(MessageFlags.IsComponentsV2);
        properties.WithAllowedMentions(new AllowedMentionsProperties().AddAllowedUsers(Context.Interaction.User.Id));
        properties.AddComponents(new TextDisplayProperties($"<@{Context.Interaction.User.Id}>"));
        properties.AddComponents(new MediaGalleryProperties().WithItems(
            [new MediaGalleryItemProperties(new ComponentMediaProperties("attachment://character_card.jpg"))]));
        properties.AddAttachments(new AttachmentProperties("character_card.jpg",
            await m_GenshinCharacterCardService.GenerateCharacterCardAsync(characterInfo, gameUid)));
        properties.AddComponents(
            new ActionRowProperties().AddButtons(new ButtonProperties($"remove_card",
                "Remove",
                ButtonStyle.Danger)));

        return properties;
    }

    private static string GetRegion(Regions server)
    {
        return server switch
        {
            Regions.Asia => "os_asia",
            Regions.Europe => "os_euro",
            Regions.America => "os_usa",
            Regions.Sar => "os_cht",
            _ => throw new ArgumentException("Invalid server name")
        };
    }
}