using MehrakCore.ApiResponseTypes;
using MehrakCore.ApiResponseTypes.Hsr;
using MehrakCore.Models;
using MehrakCore.Repositories;
using MehrakCore.Services.Commands.Executor;
using MehrakCore.Services.Common;
using MehrakCore.Services.Metrics;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;

namespace MehrakCore.Services.Commands.Hsr.CharList;

public class HsrCharListCommandExecutor : BaseCommandExecutor<HsrCharListCommandExecutor>
{
    private readonly HsrCharListCardService m_CommandService;
    private readonly HsrImageUpdaterService m_ImageUpdaterService;
    private readonly ICharacterApi<HsrBasicCharacterData, HsrCharacterInformation> m_CharacterApi;

    private Regions m_PendingServer;

    public HsrCharListCommandExecutor(ICommandService<HsrCharListCommandExecutor> commandService,
        ImageUpdaterService<HsrCharacterInformation> imageUpdaterService,
        ICharacterApi<HsrBasicCharacterData, HsrCharacterInformation> characterApi,
        UserRepository userRepository,
        TokenCacheService tokenCacheService,
        IAuthenticationMiddlewareService authenticationMiddleware,
        GameRecordApiService gameRecordApi,
        ILogger<HsrCharListCommandExecutor> logger)
        : base(userRepository, tokenCacheService, authenticationMiddleware, gameRecordApi, logger)
    {
        m_CommandService = (HsrCharListCardService)commandService;
        m_ImageUpdaterService = (HsrImageUpdaterService)imageUpdaterService;
        m_CharacterApi = characterApi;
    }

    public override async ValueTask ExecuteAsync(params object?[] parameters)
    {
        Regions? server = (Regions?)parameters[0];
        uint profile = (uint)(parameters[1] ?? 1);
        try
        {
            (UserModel? user, UserProfile? selectedProfile) = await ValidateUserAndProfileAsync(profile);
            if (user == null || selectedProfile == null) return;

            server ??= GetCachedServer(selectedProfile, GameName.HonkaiStarRail);
            if (server == null)
            {
                await SendErrorMessageAsync("No cached server found! Please select a server first.");
                return;
            }

            m_PendingServer = server.Value;

            string? ltoken = await GetOrRequestAuthenticationAsync(selectedProfile, profile);
            if (ltoken != null)
            {
                await Context.Interaction.SendResponseAsync(
                    InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
                await SendCharListCardAsync(server.Value, selectedProfile.LtUid, ltoken).ConfigureAwait(false);
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
            Logger.LogWarning("Authentication failed for user {UserId}: {ErrorMessage}", Context.Interaction.User.Id,
                result.ErrorMessage);
            return;
        }

        Context = result.Context;
        Logger.LogInformation("Authentication completed successfully for user {UserId}",
            Context.Interaction.User.Id);
        await SendCharListCardAsync(m_PendingServer, result.LtUid, result.LToken).ConfigureAwait(false);
    }

    private async ValueTask SendCharListCardAsync(Regions server, ulong ltuid, string ltoken)
    {
        try
        {
            UserModel? user = await UserRepository.GetUserAsync(Context.Interaction.User.Id);
            string region = server.GetRegion();

            ApiResult<UserGameData> response = await GetAndUpdateGameDataAsync(user, GameName.HonkaiStarRail, ltuid, ltoken, server, region);
            if (!response.IsSuccess)
                return;

            UserGameData userData = response.Data;
            string gameUid = response.Data.GameUid!;

            List<HsrCharacterInformation> characterList =
                (await m_CharacterApi.GetAllCharactersAsync(ltuid, ltoken, gameUid, region)).FirstOrDefault()?.AvatarList ?? [];
            if (characterList.Count == 0)
            {
                await SendErrorMessageAsync("No characters found for this account.");
                return;
            }

            IEnumerable<Task> avatarTask =
                characterList.Select(async x =>
                    await m_ImageUpdaterService.UpdateAvatarAsync(x.Id.ToString()!, x.Icon!));
            IEnumerable<Task> weaponTask =
                characterList.Where(x => x.Equip is not null).Select(x =>
                    m_ImageUpdaterService.UpdateEquipIconAsync(x.Equip!.Id!.Value, x.Equip!.Icon!));

            await Task.WhenAll(avatarTask);
            await Task.WhenAll(weaponTask);

            InteractionMessageProperties message = await GetCharListCardAsync(userData, characterList);
            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                .AddComponents(new TextDisplayProperties("Command execution completed")));
            await Context.Interaction.SendFollowupMessageAsync(message);
            BotMetrics.TrackCommand(Context.Interaction.User, "hsr charlist", true);
        }
        catch (CommandException e)
        {
            Logger.LogError(e, "Failed to get Character List card for user {UserId}",
                Context.Interaction.User.Id);
            await SendErrorMessageAsync(e.Message);
            BotMetrics.TrackCommand(Context.Interaction.User, "hsr charlist", false);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Failed to get Character List card for user {UserId}",
                Context.Interaction.User.Id);
            await SendErrorMessageAsync("An error occurred while generating Character List card");
            BotMetrics.TrackCommand(Context.Interaction.User, "hsr charlist", false);
        }
    }

    private async ValueTask<InteractionMessageProperties> GetCharListCardAsync(UserGameData gameData,
        List<HsrCharacterInformation> charData)
    {
        InteractionMessageProperties message = new();
        message.WithFlags(MessageFlags.IsComponentsV2);
        message.WithAllowedMentions(new AllowedMentionsProperties().AddAllowedUsers(Context.Interaction.User.Id));
        message.AddComponents(new TextDisplayProperties($"<@{Context.Interaction.User.Id}>"),
            new MediaGalleryProperties(
                [new MediaGalleryItemProperties(new ComponentMediaProperties("attachment://hsr_charlist.jpg"))]
            ));
        message.AddAttachments(new AttachmentProperties("hsr_charlist.jpg",
            await m_CommandService.GetCharListCardAsync(gameData, charData)));
        message.AddComponents(
            new ActionRowProperties().AddButtons(new ButtonProperties($"remove_card",
                "Remove", ButtonStyle.Danger)));

        return message;
    }
}
