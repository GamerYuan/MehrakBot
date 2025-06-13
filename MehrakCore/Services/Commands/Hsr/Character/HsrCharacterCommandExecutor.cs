#region

using MehrakCore.ApiResponseTypes.Hsr;
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
using NetCord.Services;

#endregion

namespace MehrakCore.Services.Commands.Hsr.Character;

public class HsrCharacterCommandExecutor : ICharacterCommandExecutor<HsrCommandModule>, IAuthenticationListener
{
    private readonly ICharacterApi<HsrBasicCharacterData, HsrCharacterInformation> m_CharacterApi;
    private readonly GameRecordApiService m_GameRecordApi;
    private readonly UserRepository m_UserRepository;
    private readonly TokenCacheService m_TokenCacheService;
    private readonly IAuthenticationMiddlewareService m_AuthenticationMiddleware;
    private readonly ImageUpdaterService<HsrCharacterInformation> m_HsrImageUpdaterService;
    private readonly ICharacterCardService<HsrCharacterInformation> m_HsrCharacterCardService;
    private readonly ILogger<HsrCharacterCommandExecutor> m_Logger;
    public IInteractionContext Context { get; set; } = null!;

    private string m_PendingCharacterName = string.Empty;
    private Regions m_PendingServer = Regions.Asia;

    public HsrCharacterCommandExecutor(ICharacterApi<HsrBasicCharacterData, HsrCharacterInformation> characterApi,
        GameRecordApiService gameRecordApi, UserRepository userRepository, TokenCacheService tokenCacheService,
        IAuthenticationMiddlewareService authenticationMiddleware,
        ImageUpdaterService<HsrCharacterInformation> hsrImageUpdaterService,
        ICharacterCardService<HsrCharacterInformation> hsrCharacterCardService,
        ILogger<HsrCharacterCommandExecutor> logger)
    {
        m_CharacterApi = characterApi;
        m_GameRecordApi = gameRecordApi;
        m_UserRepository = userRepository;
        m_TokenCacheService = tokenCacheService;
        m_AuthenticationMiddleware = authenticationMiddleware;
        m_HsrImageUpdaterService = hsrImageUpdaterService;
        m_HsrCharacterCardService = hsrCharacterCardService;
        m_Logger = logger;
    }

    public async ValueTask ExecuteAsync(params object?[] parameters)
    {
        if (parameters.Length != 3)
            throw new ArgumentException("Invalid parameters count for character command");

        var characterName = parameters[0] == null ? string.Empty : (string)parameters[0]!;
        var server = (Regions?)parameters[1];
        var profile = parameters[2] == null ? 1 : (uint)parameters[2]!;

        try
        {
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
                selectedProfile.LastUsedRegions.TryGetValue(GameName.HonkaiStarRail, out var tmp)) server = tmp;

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
            m_Logger.LogError("Error executing character command for user {UserId}: {ErrorMessage}",
                Context.Interaction.User.Id, e.Message);
            throw;
        }
    }

    private async Task SendCharacterCardResponseAsync(ulong ltuid, string ltoken, string characterName, Regions server)
    {
        try
        {
            var region = RegionUtility.GetRegion(server);
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
                !selectedProfile.GameUids.TryGetValue(GameName.HonkaiStarRail, out var dict) ||
                !dict.TryGetValue(server.ToString(), out var gameUid))
            {
                m_Logger.LogDebug("User {UserId} does not have a game UID for region {Region}",
                    Context.Interaction.User.Id, region);
                var result = await m_GameRecordApi.GetUserRegionUidAsync(ltuid, ltoken, "hkrpg_global", region);
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

            if (!selectedProfile.GameUids.ContainsKey(GameName.HonkaiStarRail))
                selectedProfile.GameUids[GameName.HonkaiStarRail] = new Dictionary<string, string>();
            if (!selectedProfile.GameUids[GameName.HonkaiStarRail].TryAdd(server.ToString(), gameUid))
                selectedProfile.GameUids[GameName.HonkaiStarRail][server.ToString()] = gameUid;

            m_Logger.LogDebug("Found game UID {GameUid} for User {UserId} in region {Region}", gameUid,
                Context.Interaction.User.Id, region);

            selectedProfile.LastUsedRegions ??= new Dictionary<GameName, Regions>();

            if (!selectedProfile.LastUsedRegions.TryAdd(GameName.HonkaiStarRail, server))
                selectedProfile.LastUsedRegions[GameName.HonkaiStarRail] = server;

            var updateUser = m_UserRepository.CreateOrUpdateUserAsync(user);
            var characterInfoTask =
                m_CharacterApi.GetAllCharactersAsync(ltuid, ltoken, gameUid, region);
            await Task.WhenAll(updateUser, characterInfoTask);

            var characterList = characterInfoTask.Result.FirstOrDefault();

            if (characterList == null)
            {
                m_Logger.LogInformation("No character data found for user {UserId} on {Region} server",
                    Context.Interaction.User.Id, region);
                await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                    .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                    .AddComponents(new TextDisplayProperties("No character data found. Please try again.")));
                return;
            }

            var characterInfo = characterList.AvatarList?
                .FirstOrDefault(x => x.Name!.Equals(characterName, StringComparison.OrdinalIgnoreCase));

            if (characterInfo == null)
            {
                m_Logger.LogInformation("Character {CharacterName} not found for user {UserId} on {Region} server",
                    characterName, Context.Interaction.User.Id, region);
                await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                    .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                    .AddComponents(new TextDisplayProperties("Character not found. Please try again.")));
                return;
            }

            await m_HsrImageUpdaterService.UpdateDataAsync(characterInfo,
                [characterList.EquipWiki!, characterList.RelicWiki!]);

            var response = await GenerateCharacterCardResponseAsync(characterInfo, gameUid);
            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                .AddComponents(new TextDisplayProperties("Command execution completed")));
            await Context.Interaction.SendFollowupMessageAsync(response);
            BotMetrics.TrackCommand(Context.Interaction.User, "hsr character", true);
            BotMetrics.TrackCharacterSelection(nameof(GameName.HonkaiStarRail), characterName);
        }
        catch (Exception e)
        {
            m_Logger.LogError(
                "Error sending character card response with character {CharacterName} for user {UserId}: {ErrorMessage}",
                characterName, Context.Interaction.User.Id, e.Message);
            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                .AddComponents(new TextDisplayProperties($"An unknown error occurred, please try again later.")));
            BotMetrics.TrackCommand(Context.Interaction.User, "hsr character", false);
        }
    }

    public async Task OnAuthenticationCompletedAsync(AuthenticationResult result)
    {
        if (result.IsSuccess)
        {
            Context = result.Context;
            m_Logger.LogInformation("Authentication completed successfully for user {UserId}",
                Context.Interaction.User.Id);
            await SendCharacterCardResponseAsync(result.LtUid, result.LToken, m_PendingCharacterName,
                m_PendingServer);
        }
        else
        {
            m_Logger.LogWarning("Authentication failed for user {UserId}: {ErrorMessage}",
                Context.Interaction.User.Id, result.ErrorMessage);
            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .AddComponents(new TextDisplayProperties($"Authentication failed: {result.ErrorMessage}"))
                .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2));
        }
    }

    private async Task<InteractionMessageProperties> GenerateCharacterCardResponseAsync(
        HsrCharacterInformation characterInfo, string gameUid)
    {
        InteractionMessageProperties properties = new();
        properties.WithFlags(MessageFlags.IsComponentsV2);
        properties.WithAllowedMentions(new AllowedMentionsProperties().AddAllowedUsers(Context.Interaction.User.Id));
        properties.AddComponents(new TextDisplayProperties($"<@{Context.Interaction.User.Id}>"));
        properties.AddComponents(new MediaGalleryProperties().WithItems(
            [new MediaGalleryItemProperties(new ComponentMediaProperties("attachment://character_card.jpg"))]));
        properties.AddAttachments(new AttachmentProperties("character_card.jpg",
            await m_HsrCharacterCardService.GenerateCharacterCardAsync(characterInfo, gameUid)));
        properties.AddComponents(
            new ActionRowProperties().AddButtons(new ButtonProperties($"remove_card",
                "Remove",
                ButtonStyle.Danger)));

        return properties;
    }
}
