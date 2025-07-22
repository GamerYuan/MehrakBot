#region

using MehrakCore.ApiResponseTypes;
using MehrakCore.ApiResponseTypes.Genshin;
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

namespace MehrakCore.Services.Commands.Genshin.CharList;

public class GenshinCharListCommandExecutor : BaseCommandExecutor<GenshinCommandModule>
{
    private readonly GenshinCharListCardService m_CommandService;
    private readonly GenshinImageUpdaterService m_ImageUpdaterService;
    private readonly ICharacterApi<GenshinBasicCharacterData, GenshinCharacterDetail> m_CharacterApi;
    private Regions m_PendingServer;

    public GenshinCharListCommandExecutor(ICommandService<GenshinCharListCommandExecutor> commandService,
        GenshinImageUpdaterService imageUpdaterService,
        ICharacterApi<GenshinBasicCharacterData, GenshinCharacterDetail> characterApi,
        UserRepository userRepository, TokenCacheService tokenCacheService,
        IAuthenticationMiddlewareService authenticationMiddleware, GameRecordApiService gameRecordApi,
        ILogger<GenshinCommandModule> logger) : base(userRepository, tokenCacheService, authenticationMiddleware,
        gameRecordApi, logger)
    {
        m_CommandService = (GenshinCharListCardService)commandService;
        m_ImageUpdaterService = imageUpdaterService;
        m_CharacterApi = characterApi;
    }

    public override async ValueTask ExecuteAsync(params object?[] parameters)
    {
        var server = (Regions?)parameters[0];
        var profile = (uint)(parameters[1] ?? 1);
        try
        {
            var (user, selectedProfile) = await ValidateUserAndProfileAsync(profile);
            if (user == null || selectedProfile == null) return;

            server ??= GetCachedServer(selectedProfile, GameName.Genshin);
            if (server == null)
            {
                await SendErrorMessageAsync("No cached server found! Please select a server first.");
                return;
            }

            m_PendingServer = server.Value;

            var ltoken = await GetOrRequestAuthenticationAsync(selectedProfile, profile);
            if (ltoken != null)
            {
                await Context.Interaction.SendResponseAsync(
                    InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
                await GetCharListCardAsync(server.Value, selectedProfile.LtUid, ltoken).ConfigureAwait(false);
            }
        }
        catch (CommandException e)
        {
            Logger.LogError(e, "Command execution failed for region: {Region}, profile: {Profile}", server, profile);
            await SendErrorMessageAsync(e.Message);
        }
        catch (Exception e)
        {
            Logger.LogError(e,
                "An unexpected error occurred while executing command for region: {Region}, profile: {Profile}",
                server, profile);
            await SendErrorMessageAsync();
        }
    }

    public override async Task OnAuthenticationCompletedAsync(AuthenticationResult result)
    {
        if (!result.IsSuccess)
        {
            Logger.LogError("Authentication failed for user {UserId}: {ErrorMessage}", Context.Interaction.User.Id,
                result.ErrorMessage);
            await SendAuthenticationErrorAsync(result.ErrorMessage);
            return;
        }

        Context = result.Context;
        Logger.LogInformation("Authentication completed successfully for user {UserId}",
            Context.Interaction.User.Id);
        await GetCharListCardAsync(m_PendingServer, result.LtUid, result.LToken).ConfigureAwait(false);
    }

    private async ValueTask GetCharListCardAsync(Regions server, ulong ltuid, string ltoken)
    {
        try
        {
            var user = await UserRepository.GetUserAsync(Context.Interaction.User.Id);
            var region = server.GetRegion();

            var response = await GetAndUpdateGameDataAsync(user, GameName.Genshin, ltuid, ltoken, server, region);
            if (!response.IsSuccess)
                return;

            var userData = response.Data;
            var gameUid = response.Data.GameUid!;

            var characterList = (await m_CharacterApi.GetAllCharactersAsync(ltuid, ltoken, gameUid, region)).ToList();
            if (characterList.Count == 0)
            {
                await SendErrorMessageAsync("No characters found for this account.");
                return;
            }

            var avatarTask =
                characterList.Select(async x =>
                    await m_ImageUpdaterService.UpdateAvatarAsync(x.Id.ToString()!, x.Icon!));
            var weaponTask =
                characterList.Select(async x =>
                    await m_ImageUpdaterService.UpdateWeaponImageTask(x.Weapon));

            await Task.WhenAll(avatarTask);
            await Task.WhenAll(weaponTask);

            var message = await GetCardAsync(userData, characterList);
            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                .AddComponents(new TextDisplayProperties("Command execution completed")));
            await Context.Interaction.SendFollowupMessageAsync(message);
            BotMetrics.TrackCommand(Context.Interaction.User, "genshin charlist", true);
        }
        catch (CommandException e)
        {
            Logger.LogError(e, "Failed to get Character List card for user {UserId}",
                Context.Interaction.User.Id);
            await SendErrorMessageAsync(e.Message);
            BotMetrics.TrackCommand(Context.Interaction.User, "genshin charlist", false);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Failed to get Character List card for user {UserId}",
                Context.Interaction.User.Id);
            await SendErrorMessageAsync("An error occurred while generating Character List card");
            BotMetrics.TrackCommand(Context.Interaction.User, "genshin charlist", false);
        }
    }

    private async ValueTask<InteractionMessageProperties> GetCardAsync(UserGameData gameData,
        List<GenshinBasicCharacterData> characters)
    {
        InteractionMessageProperties message = new();
        message.WithFlags(MessageFlags.IsComponentsV2);
        message.WithAllowedMentions(
            new AllowedMentionsProperties().AddAllowedUsers(Context.Interaction.User.Id));
        message.AddComponents(new TextDisplayProperties($"<@{Context.Interaction.User.Id}>"));
        message.AddComponents(new MediaGalleryProperties([
            new MediaGalleryItemProperties(new ComponentMediaProperties("attachment://charlist.jpg"))
        ]));
        message.AddAttachments(new AttachmentProperties("charlist.jpg",
            await m_CommandService.GetCharListCardAsync(gameData, characters)));

        return message;
    }
}
