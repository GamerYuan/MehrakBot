using Mehrak.Domain.Services.Abstractions;
using MehrakCore.ApiResponseTypes.Zzz;
using MehrakCore.Models;
using MehrakCore.Modules;
using MehrakCore.Services.Commands.Executor;
using MehrakCore.Services.Common;
using MehrakCore.Services.Metrics;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;

namespace MehrakCore.Services.Commands.Zzz.Character;

public class ZzzCharacterCommandExecutor : BaseCommandExecutor<ZzzCharacterCommandExecutor>, ICharacterCommandExecutor<ZzzCommandModule>
{
    private readonly ICharacterApi<ZzzBasicAvatarData, ZzzFullAvatarData> m_CharacterApi;
    private readonly ICharacterCacheService m_CharacterCacheService;
    private readonly ICharacterCardService<ZzzFullAvatarData> m_CharacterCardService;
    private readonly ImageUpdaterService<ZzzFullAvatarData> m_ImageUpdaterService;
    private string m_PendingCharacterName = string.Empty;
    private Regions m_PendingServer = Regions.Asia;

    public ZzzCharacterCommandExecutor(
        ICharacterApi<ZzzBasicAvatarData, ZzzFullAvatarData> characterApi,
        ICharacterCacheService characterCacheService,
        ICharacterCardService<ZzzFullAvatarData> characterCardService,
        ImageUpdaterService<ZzzFullAvatarData> imageUpdaterService,
        UserRepository userRepository,
        RedisCacheService tokenCacheService,
        IAuthenticationMiddlewareService authenticationMiddleware,
        GameRecordApiService gameRecordApi,
        ILogger<ZzzCharacterCommandExecutor> logger)
        : base(userRepository, tokenCacheService, authenticationMiddleware, gameRecordApi, logger)
    {
        m_CharacterApi = characterApi;
        m_CharacterCacheService = characterCacheService;
        m_CharacterCardService = characterCardService;
        m_ImageUpdaterService = imageUpdaterService;
    }

    public override async ValueTask ExecuteAsync(params object?[] parameters)
    {
        if (parameters.Length != 3)
            throw new ArgumentException("Invalid parameters count for character command");

        string characterName = parameters[0] == null ? string.Empty : (string)parameters[0]!;
        Regions? server = (Regions?)parameters[1];
        uint profile = parameters[2] == null ? 1 : (uint)parameters[2]!;

        try
        {
            (UserModel? user, UserProfile? selectedProfile) = await ValidateUserAndProfileAsync(profile);
            if (user == null || selectedProfile == null)
                return;

            // Try to get cached server or use provided server
            server ??= GetCachedServer(selectedProfile, Game.HonkaiStarRail);

            if (!await ValidateServerAsync(server))
                return;

            m_PendingCharacterName = characterName;
            m_PendingServer = server!.Value;

            string? ltoken = await GetOrRequestAuthenticationAsync(selectedProfile, profile);

            if (ltoken != null)
            {
                Logger.LogInformation("User {UserId} is already authenticated", Context.Interaction.User.Id);
                await Context.Interaction.SendResponseAsync(
                    InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
                await SendCharacterCardResponseAsync(selectedProfile.LtUid, ltoken, characterName,
                    server.Value);
            }
        }
        catch (CommandException e)
        {
            Logger.LogError(e, "Error executing character command for user {UserId}",
                Context.Interaction.User.Id);
            await SendErrorMessageAsync(e.Message);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error executing character command for user {UserId}",
                Context.Interaction.User.Id);
            await SendErrorMessageAsync();
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
        }
    }

    private async ValueTask SendCharacterCardResponseAsync(ulong ltuid, string ltoken,
        string characterName, Regions server)
    {
        try
        {
            Logger.LogInformation("Executing character card command request for user {UserId} for character {CharacterName}",
                Context.Interaction.User.Id, characterName);
            string region = server.GetRegion();
            UserModel? user = await UserRepository.GetUserAsync(Context.Interaction.User.Id);

            Result<string> result = await GetAndUpdateGameUidAsync(user, Game.ZenlessZoneZero, ltuid, ltoken, server, region);
            if (!result.IsSuccess) return;
            string gameUid = result.Data;

            ZzzBasicAvatarData[] characters = [.. await m_CharacterApi.GetAllCharactersAsync(ltuid, ltoken, gameUid, region)];

            ZzzBasicAvatarData? character = characters.FirstOrDefault(x =>
                x.Name.Equals(characterName, StringComparison.OrdinalIgnoreCase) ||
                x.FullName.Equals(characterName, StringComparison.OrdinalIgnoreCase));

            if (character == null)
            {
                m_CharacterCacheService.GetAliases(Game.ZenlessZoneZero).TryGetValue(characterName, out string? name);

                if (name == null ||
                    (character =
                        characters.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) ==
                    null)
                {
                    await SendErrorMessageAsync("Character not found. Please try again.");
                    return;
                }
            }

            Result<ZzzFullAvatarData> response = await
                m_CharacterApi.GetCharacterDataFromIdAsync(ltuid, ltoken, gameUid, region, (uint)character.Id!);
            if (!response.IsSuccess)
            {
                await SendErrorMessageAsync(response.ErrorMessage);
                return;
            }
            ZzzFullAvatarData characterData = response.Data;
            if (characterData == null)
            {
                await SendErrorMessageAsync("Character data not found. Please try again.");
                return;
            }
            Logger.LogInformation("Successfully retrieved character data for {CharacterName} for user {UserId}",
                character.Name, Context.Interaction.User.Id);

            await m_ImageUpdaterService.UpdateDataAsync(characterData, []);

            InteractionMessageProperties message = await GetCharacterCardAsync(characterData, gameUid);
            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithFlags(MessageFlags.IsComponentsV2 | MessageFlags.Ephemeral)
                .AddComponents(new TextDisplayProperties("Command execution completed")));
            await Context.Interaction.SendFollowupMessageAsync(message);
            BotMetrics.TrackCommand(Context.Interaction.User, "zzz character", true);
            BotMetrics.TrackCharacterSelection(nameof(Game.ZenlessZoneZero), character.Name);
        }
        catch (CommandException e)
        {
            Logger.LogError(e, "Error sending character card response with character {CharacterName} for user {UserId}",
                characterName, Context.Interaction.User.Id);
            await SendErrorMessageAsync(e.Message);
            BotMetrics.TrackCommand(Context.Interaction.User, "zzz character", false);
        }
        catch (Exception e)
        {
            Logger.LogError(e,
                "Error sending character card response with character {CharacterName} for user {UserId}",
                characterName, Context.Interaction.User.Id);
            await SendErrorMessageAsync();
            BotMetrics.TrackCommand(Context.Interaction.User, "zzz character", false);
        }
    }

    private async ValueTask<InteractionMessageProperties> GetCharacterCardAsync(ZzzFullAvatarData characterData,
        string gameUid)
    {
        InteractionMessageProperties properties = new();
        properties.WithFlags(MessageFlags.IsComponentsV2);
        properties.WithAllowedMentions(
            new AllowedMentionsProperties().AddAllowedUsers(Context.Interaction.User.Id));
        properties.AddComponents(new TextDisplayProperties($"<@{Context.Interaction.User.Id}>"));
        properties.AddComponents(new MediaGalleryProperties().WithItems(
            [new MediaGalleryItemProperties(new ComponentMediaProperties("attachment://character_card.jpg"))]));
        properties.AddAttachments(new AttachmentProperties("character_card.jpg",
            await m_CharacterCardService.GenerateCharacterCardAsync(characterData, gameUid)));
        properties.AddComponents(
            new ActionRowProperties().AddButtons(new ButtonProperties($"remove_card",
                "Remove",
                ButtonStyle.Danger)));

        return properties;
    }
}
