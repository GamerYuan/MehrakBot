#region

using Mehrak.Domain.Interfaces;
using MehrakCore.ApiResponseTypes;
using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Models;
using MehrakCore.Modules;
using MehrakCore.Services.Commands.Executor;
using MehrakCore.Services.Common;
using MehrakCore.Services.Metrics;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;

#endregion

namespace MehrakCore.Services.Commands.Genshin.Stygian;

public class GenshinStygianCommandExecutor : BaseCommandExecutor<GenshinCommandModule>
{
    private readonly GenshinImageUpdaterService m_ImageUpdaterService;
    private readonly GenshinStygianApiService m_ApiService;
    private readonly GenshinStygianCardService m_CommandService;

    private Regions m_PendingServer;

    public GenshinStygianCommandExecutor(GenshinImageUpdaterService imageUpdaterService,
        ICommandService<GenshinStygianCommandExecutor> commandService,
        IApiService<GenshinStygianCommandExecutor> apiService,
        UserRepository userRepository, RedisCacheService tokenCacheService,
        IAuthenticationMiddlewareService authenticationMiddleware, GameRecordApiService gameRecordApi,
        ILogger<GenshinCommandModule> logger) : base(userRepository, tokenCacheService, authenticationMiddleware,
        gameRecordApi, logger)
    {
        m_ImageUpdaterService = imageUpdaterService;
        m_ApiService = (GenshinStygianApiService)apiService;
        m_CommandService = (GenshinStygianCardService)commandService;
    }

    public override async ValueTask ExecuteAsync(params object?[] parameters)
    {
        var server = (Regions?)parameters[0];
        var profile = (uint)(parameters[1] ?? 1);
        try
        {
            var (user, selectedProfile) = await ValidateUserAndProfileAsync(profile);
            if (user == null || selectedProfile == null) return;

            server ??= GetCachedServer(selectedProfile, Game.Genshin);
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
                await SendStygianCardAsync(server.Value, selectedProfile.LtUid, ltoken).ConfigureAwait(false);
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
        await SendStygianCardAsync(m_PendingServer, result.LtUid, result.LToken).ConfigureAwait(false);
    }

    private async ValueTask SendStygianCardAsync(Regions server, ulong ltuid, string ltoken)
    {
        try
        {
            var user = await UserRepository.GetUserAsync(Context.Interaction.User.Id);
            var region = server.GetRegion();

            var response = await GetAndUpdateGameDataAsync(user, Game.Genshin, ltuid, ltoken, server, region);
            if (!response.IsSuccess)
                return;

            var userData = response.Data;
            var gameUid = response.Data.GameUid!;

            var stygianInfo = await m_ApiService.GetStygianDataAsync(gameUid, region, ltuid, ltoken);
            if (!stygianInfo.IsSuccess)
            {
                await SendErrorMessageAsync(stygianInfo.ErrorMessage);
                return;
            }

            if (!stygianInfo.Data.IsUnlock)
            {
                Logger.LogWarning("Stygian Onslaught is not unlocked for user {UserId}", Context.Interaction.User.Id);
                await SendErrorMessageAsync("Stygian Onslaught is not unlocked");
                return;
            }

            if (!stygianInfo.Data.Data![0].Single.HasData)
            {
                Logger.LogWarning("No Stygian Onslaught data found for this cycle for user {UserId}",
                    Context.Interaction.User.Id);
                await SendErrorMessageAsync("No Stygian Onslaught data found for this cycle");
                return;
            }

            var stygianData = stygianInfo.Data.Data[0].Single;

            var avatarTasks = stygianData.Challenge!.SelectMany(x => x.Teams).Select(async x =>
                await m_ImageUpdaterService.UpdateAvatarAsync(x.AvatarId.ToString(), x.Image));
            var sideAvatarTasks = stygianData.Challenge!.SelectMany(x => x.BestAvatar).Select(async x =>
                await m_ImageUpdaterService.UpdateSideAvatarAsync(x.AvatarId.ToString(), x.SideIcon));
            var monsterImages = await stygianData.Challenge!.Select(x => x.Monster).ToAsyncEnumerable()
                .ToDictionaryAwaitAsync(async x => await Task.FromResult(x.MonsterId),
                    async x => await m_ApiService.GetMonsterImageAsync(x.Icon));

            await Task.WhenAll(avatarTasks);
            await Task.WhenAll(sideAvatarTasks);

            var stygianCard = await GetStygianCardAsync(stygianInfo.Data.Data[0], server, userData, monsterImages);

            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                .AddComponents(new TextDisplayProperties("Command execution completed")));
            await Context.Interaction.SendFollowupMessageAsync(stygianCard);
            BotMetrics.TrackCommand(Context.Interaction.User, "genshin stygian", true);
        }
        catch (CommandException e)
        {
            Logger.LogError(e, "Failed to get Stygian Onslaught card for user {UserId}",
                Context.Interaction.User.Id);
            await SendErrorMessageAsync(e.Message);
            BotMetrics.TrackCommand(Context.Interaction.User, "genshin stygian", false);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Failed to get Stygian Onslaught card for user {UserId}",
                Context.Interaction.User.Id);
            await SendErrorMessageAsync("An error occurred while generating Stygian Onslaught card");
            BotMetrics.TrackCommand(Context.Interaction.User, "genshin stygian", false);
        }
    }

    private async ValueTask<InteractionMessageProperties> GetStygianCardAsync(StygianData stygianData,
        Regions region, UserGameData gameData, Dictionary<int, Stream> monsterImages)
    {
        try
        {
            var message = new InteractionMessageProperties()
                .WithFlags(MessageFlags.IsComponentsV2);
            ComponentContainerProperties container =
            [
                new TextDisplayProperties($"### <@{Context.Interaction.User.Id}>'s Stygian Onslaught Summary"),
                new TextDisplayProperties(
                    $"Cycle start: <t:{stygianData.Schedule!.StartTime}:f>\nCycle end: <t:{stygianData.Schedule!.EndTime}:f>"),
                new MediaGalleryProperties().AddItems(
                    new MediaGalleryItemProperties(new ComponentMediaProperties("attachment://stygian_card.jpg"))),
                new TextDisplayProperties(
                    "-# Information may be inaccurate due to API limitations. Please check in-game for the most accurate data.")
            ];

            message.AddAttachments(new AttachmentProperties("stygian_card.jpg",
                await m_CommandService.GetStygianCardImageAsync(stygianData, gameData, region, monsterImages)));

            message.AddComponents([container]);
            message.AddComponents(
                new ActionRowProperties().AddButtons(new ButtonProperties("remove_card",
                    "Remove", ButtonStyle.Danger)));
            return message;
        }
        catch (CommandException)
        {
            throw;
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Failed to generate Stygian Onslaught card for user {UserId}",
                Context.Interaction.User.Id);
            throw new CommandException("An error occurred while generating Stygian Onslaught card", e);
        }
    }
}
