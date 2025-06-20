#region

using MehrakCore.ApiResponseTypes.Hsr;
using MehrakCore.Models;
using MehrakCore.Modules;
using MehrakCore.Repositories;
using MehrakCore.Services.Commands.Executor;
using MehrakCore.Services.Common;
using MehrakCore.Services.Metrics;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;

#endregion

namespace MehrakCore.Services.Commands.Hsr.Character;

public class HsrCharacterCommandExecutor : BaseCommandExecutor<HsrCharacterCommandExecutor>,
    ICharacterCommandExecutor<HsrCommandModule>
{
    private readonly ICharacterApi<HsrBasicCharacterData, HsrCharacterInformation> m_CharacterApi;
    private readonly ImageUpdaterService<HsrCharacterInformation> m_HsrImageUpdaterService;
    private readonly ICharacterCardService<HsrCharacterInformation> m_HsrCharacterCardService;

    private string m_PendingCharacterName = string.Empty;
    private Regions m_PendingServer = Regions.Asia;

    public HsrCharacterCommandExecutor(ICharacterApi<HsrBasicCharacterData, HsrCharacterInformation> characterApi,
        GameRecordApiService gameRecordApi, UserRepository userRepository, TokenCacheService tokenCacheService,
        IAuthenticationMiddlewareService authenticationMiddleware,
        ImageUpdaterService<HsrCharacterInformation> hsrImageUpdaterService,
        ICharacterCardService<HsrCharacterInformation> hsrCharacterCardService,
        ILogger<HsrCharacterCommandExecutor> logger)
        : base(userRepository, tokenCacheService, authenticationMiddleware, gameRecordApi, logger)
    {
        m_CharacterApi = characterApi;
        m_HsrImageUpdaterService = hsrImageUpdaterService;
        m_HsrCharacterCardService = hsrCharacterCardService;
    }

    public override async ValueTask ExecuteAsync(params object?[] parameters)
    {
        if (parameters.Length != 3)
            throw new ArgumentException("Invalid parameters count for character command");

        var characterName = parameters[0] == null ? string.Empty : (string)parameters[0]!;
        var server = (Regions?)parameters[1];
        var profile = parameters[2] == null ? 1 : (uint)parameters[2]!;
        try
        {
            var (user, selectedProfile) = await ValidateUserAndProfileAsync(profile);
            if (user == null || selectedProfile == null)
                return;

            // Try to get cached server or use provided server
            if (!server.HasValue)
                server = GetCachedServer(selectedProfile, GameName.HonkaiStarRail);

            if (!await ValidateServerAsync(server))
                return;

            m_PendingCharacterName = characterName;
            m_PendingServer = server!.Value;

            var ltoken = await GetOrRequestAuthenticationAsync(selectedProfile, profile);

            if (ltoken != null)
            {
                Logger.LogInformation("User {UserId} is already authenticated", Context.Interaction.User.Id);
                await Context.Interaction.SendResponseAsync(
                    InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
                await SendCharacterCardResponseAsync(selectedProfile.LtUid, ltoken, characterName,
                    server!.Value);
            }
        }
        catch (Exception e)
        {
            Logger.LogError("Error executing character command for user {UserId}: {ErrorMessage}",
                Context.Interaction.User.Id, e.Message);
            throw;
        }
    }

    private async Task SendCharacterCardResponseAsync(ulong ltuid, string ltoken, string characterName, Regions server)
    {
        try
        {
            var region = RegionUtility.GetRegion(server);
            var user = await UserRepository.GetUserAsync(Context.Interaction.User.Id);

            var result = await GetAndUpdateGameDataAsync(user, GameName.HonkaiStarRail, ltuid, ltoken, server,
                region);

            if (!result.IsSuccess) return;

            var gameUid = result.Data.GameUid!;
            var characterList = (await m_CharacterApi.GetAllCharactersAsync(ltuid, ltoken, gameUid, region))
                .FirstOrDefault();

            if (characterList == null)
            {
                Logger.LogInformation("No character data found for user {UserId} on {Region} server",
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
                Logger.LogInformation("Character {CharacterName} not found for user {UserId} on {Region} server",
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
            Logger.LogError(
                "Error sending character card response with character {CharacterName} for user {UserId}: {ErrorMessage}",
                characterName, Context.Interaction.User.Id, e.Message);
            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                .AddComponents(new TextDisplayProperties($"An unknown error occurred, please try again later.")));
            BotMetrics.TrackCommand(Context.Interaction.User, "hsr character", false);
        }
    }

    public override async Task OnAuthenticationCompletedAsync(AuthenticationResult result)
    {
        if (result.IsSuccess)
        {
            Context = result.Context;
            Logger.LogInformation("Authentication completed successfully for user {UserId}",
                Context.Interaction.User.Id);
            await SendCharacterCardResponseAsync(result.LtUid, result.LToken, m_PendingCharacterName,
                m_PendingServer);
        }
        else
        {
            Logger.LogWarning("Authentication failed for user {UserId}: {ErrorMessage}",
                Context.Interaction.User.Id, result.ErrorMessage);
            await SendAuthenticationErrorAsync(result.ErrorMessage);
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
