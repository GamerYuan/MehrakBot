using MehrakCore.ApiResponseTypes;
using MehrakCore.ApiResponseTypes.Zzz;
using MehrakCore.Models;
using MehrakCore.Repositories;
using MehrakCore.Services.Commands.Executor;
using MehrakCore.Services.Common;
using MehrakCore.Services.Metrics;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;

namespace MehrakCore.Services.Commands.Zzz.Defense;

public class ZzzDefenseCommandExecutor : BaseCommandExecutor<ZzzDefenseCommandExecutor>
{
    private readonly ZzzDefenseApiService m_ApiService;
    private readonly ZzzDefenseCardService m_CommandService;
    private readonly ZzzImageUpdaterService m_ImageUpdaterService;
    private Regions m_PendingServer;

    public ZzzDefenseCommandExecutor(
        IApiService<ZzzDefenseCommandExecutor> apiService,
        ICommandService<ZzzDefenseCommandExecutor> commandService,
        ImageUpdaterService<ZzzFullAvatarData> imageUpdaterService,
        UserRepository userRepository,
        TokenCacheService tokenCacheService,
        IAuthenticationMiddlewareService authenticationMiddleware,
        GameRecordApiService gameRecordApi,
        ILogger<ZzzDefenseCommandExecutor> logger)
        : base(userRepository, tokenCacheService, authenticationMiddleware, gameRecordApi, logger)
    {
        m_ApiService = (ZzzDefenseApiService)apiService;
        m_CommandService = (ZzzDefenseCardService)commandService;
        m_ImageUpdaterService = (ZzzImageUpdaterService)imageUpdaterService;
    }

    public override async ValueTask ExecuteAsync(params object?[] parameters)
    {
        if (parameters.Length != 2)
            throw new ArgumentException("Invalid number of parameters provided.");

        Regions? server = (Regions?)parameters[0];
        uint profile = (uint)(parameters[1] ?? 1);

        try
        {
            (UserModel? user, UserProfile? selectedProfile) = await ValidateUserAndProfileAsync(profile);
            if (user == null || selectedProfile == null) return;

            server ??= GetCachedServer(selectedProfile, GameName.ZenlessZoneZero);
            if (server == null)
            {
                await SendErrorMessageAsync("No cached server found! Please select a server first.", false);
                return;
            }

            m_PendingServer = server.Value;

            string? ltoken = await GetOrRequestAuthenticationAsync(selectedProfile, profile);
            if (ltoken != null)
            {
                await Context.Interaction.SendResponseAsync(
                    InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
                await SendDefenseCardAsync(server.Value, selectedProfile.LtUid, ltoken).ConfigureAwait(false);
            }
        }
        catch (CommandException e)
        {
            Logger.LogError(e, "Error processing Shiyu Defense command for user {UserId} profile {Profile}",
                Context.Interaction.User.Id, profile);
            await SendErrorMessageAsync(e.Message, false);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing Shiyu Defense command for user {UserId} profile {Profile}",
                Context.Interaction.User.Id, profile);
            await SendErrorMessageAsync(followup: false);
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
        await SendDefenseCardAsync(m_PendingServer, result.LtUid, result.LToken).ConfigureAwait(false);
    }

    private async ValueTask SendDefenseCardAsync(Regions server, ulong ltuid, string ltoken)
    {
        try
        {
            string region = server.GetRegion();
            UserModel? user = await UserRepository.GetUserAsync(Context.Interaction.User.Id);
            ApiResult<ApiResponseTypes.UserGameData> response =
                await GetAndUpdateGameDataAsync(user, GameName.ZenlessZoneZero, ltuid, ltoken, server, region);
            if (!response.IsSuccess)
                return;

            ZzzDefenseData defenseData = await m_ApiService.GetDefenseDataAsync(ltoken, ltuid,
                response.Data.GameUid!, region);

            if (!defenseData.HasData)
            {
                Logger.LogInformation("No Shiyu Defense clear records found for user {UserId}",
                    Context.Interaction.User.Id);
                await SendErrorMessageAsync("No Shiyu Defense clear records found");
                return;
            }

            FloorDetail[] nonNull = [.. defenseData.AllFloorDetail.Where(x => x is { Node1: not null, Node2: not null })];
            if (nonNull.Length == 0)
            {
                Logger.LogInformation("No Shiyu Defense clear records found for user {UserId}",
                    Context.Interaction.User.Id);
                await SendErrorMessageAsync("No Shiyu Defense clear records found");
                return;
            }

            IEnumerable<Task> updateImageTask = nonNull.SelectMany(x => x.Node1.Avatars.Concat(x.Node2.Avatars))
                .DistinctBy(x => x!.Id)
                .Select(async avatar => await m_ImageUpdaterService.UpdateAvatarAsync(avatar.Id.ToString(), avatar.RoleSquareUrl));
            IEnumerable<Task> updateBuddyTask = nonNull.SelectMany(x => new ZzzBuddy?[] { x.Node1.Buddy, x.Node2.Buddy })
                .Where(x => x is not null)
                .DistinctBy(x => x!.Id)
                .Select(async buddy => await m_ImageUpdaterService.UpdateBuddyImageAsync(buddy!.Id, buddy.BangbooRectangleUrl));

            await Task.WhenAll(updateImageTask);
            await Task.WhenAll(updateBuddyTask);

            InteractionMessageProperties message = await GetMessageAsync(defenseData, response.Data, server);
            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                .AddComponents(new TextDisplayProperties("Command execution completed")));
            await Context.Interaction.SendFollowupMessageAsync(message);
            BotMetrics.TrackCommand(Context.Interaction.User, "zzz shiyu", true);
        }
        catch (CommandException e)
        {
            BotMetrics.TrackCommand(Context.Interaction.User, "zzz shiyu", false);
            Logger.LogError(e, "Error fetching Zzz Defense data for user {UserId}", Context.Interaction.User.Id);
            await SendErrorMessageAsync(e.Message, false);
        }
        catch (Exception ex)
        {
            BotMetrics.TrackCommand(Context.Interaction.User, "zzz shiyu", false);
            Logger.LogError(ex, "Unexpected error fetching Zzz Defense data for user {UserId}", Context.Interaction.User.Id);
            await SendErrorMessageAsync(followup: false);
        }
    }

    private async ValueTask<InteractionMessageProperties> GetMessageAsync(ZzzDefenseData data, UserGameData gameData, Regions region)
    {
        InteractionMessageProperties message = new();
        message.WithFlags(MessageFlags.IsComponentsV2);
        ComponentContainerProperties container =
            [
                new TextDisplayProperties(
                    $"### <@{Context.Interaction.User.Id}>'s Shiyu Defense Summary"),
                new TextDisplayProperties(
                    $"Cycle start: <t:{data.BeginTime}:f>\nCycle end: <t:{data.EndTime}:f>"),
                new MediaGalleryProperties().AddItems(
                    new MediaGalleryItemProperties(new ComponentMediaProperties($"attachment://defense_card.jpg"))),
                new TextDisplayProperties(
                    $"-# Information may be inaccurate due to API limitations. Please check in-game for the most accurate data.")
            ];
        message.WithComponents([container]);
        message.AddAttachments(new AttachmentProperties($"defense_card.jpg",
            await m_CommandService.GetDefenseCardAsync(data, gameData, region)));

        return message;
    }
}
