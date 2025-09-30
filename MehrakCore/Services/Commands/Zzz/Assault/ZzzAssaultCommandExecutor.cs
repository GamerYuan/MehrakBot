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

namespace MehrakCore.Services.Commands.Zzz.Assault;

public class ZzzAssaultCommandExecutor : BaseCommandExecutor<ZzzAssaultCommandExecutor>
{
    private readonly ZzzAssaultApiService m_ApiService;
    private readonly ZzzAssaultCardService m_CommandService;
    private readonly ZzzImageUpdaterService m_ImageUpdaterService;

    private Regions m_PendingServer;

    public ZzzAssaultCommandExecutor(
        IApiService<ZzzAssaultCommandExecutor> apiService,
        ICommandService<ZzzAssaultCommandExecutor> cardService,
        ImageUpdaterService<ZzzFullAvatarData> imageUpdaterService,
        UserRepository userRepository,
        TokenCacheService tokenCacheService,
        IAuthenticationMiddlewareService authenticationMiddleware,
        GameRecordApiService gameRecordApi,
        ILogger<ZzzAssaultCommandExecutor> logger) :
        base(userRepository, tokenCacheService, authenticationMiddleware, gameRecordApi, logger)
    {
        m_ApiService = (ZzzAssaultApiService)apiService;
        m_CommandService = (ZzzAssaultCardService)cardService;
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
                await SendAssaultCardAsync(server.Value, selectedProfile.LtUid, ltoken).ConfigureAwait(false);
            }
        }
        catch (CommandException e)
        {
            Logger.LogError(e, "Error processing Deadly Assault command for user {UserId} profile {Profile}",
                Context.Interaction.User.Id, profile);
            await SendErrorMessageAsync(e.Message, false);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing Deadly Assault command for user {UserId} profile {Profile}",
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
        await SendAssaultCardAsync(m_PendingServer, result.LtUid, result.LToken).ConfigureAwait(false);
    }

    private async ValueTask SendAssaultCardAsync(Regions server, ulong ltuid, string ltoken)
    {
        try
        {
            string region = server.GetRegion();
            UserModel? user = await UserRepository.GetUserAsync(Context.Interaction.User.Id);
            ApiResult<UserGameData> response =
                await GetAndUpdateGameDataAsync(user, GameName.ZenlessZoneZero, ltuid, ltoken, server, region);
            if (!response.IsSuccess)
                return;

            ZzzAssaultData assaultData = await m_ApiService.GetAssaultDataAsync(ltoken, ltuid,
                response.Data.GameUid!, region);

            if (!assaultData.HasData || assaultData.List.Count == 0)
            {
                Logger.LogInformation("No Deadly Assault clear records found for user {UserId}",
                    Context.Interaction.User.Id);
                await SendErrorMessageAsync("No Deadly Assault clear records found");
                return;
            }

            IEnumerable<Task> avatarImageTask = assaultData.List.SelectMany(x => x.AvatarList)
                .DistinctBy(x => x.Id)
                .Select(async avatar => await m_ImageUpdaterService.UpdateAvatarAsync(avatar.Id.ToString(), avatar.RoleSquareUrl));
            IEnumerable<Task> buddyImageTask = assaultData.List.Select(x => x.Buddy)
                .Where(x => x is not null)
                .DistinctBy(x => x!.Id)
                .Select(async buddy => await m_ImageUpdaterService.UpdateBuddyImageAsync(buddy!.Id, buddy!.BangbooRectangleUrl));
            IEnumerable<Task<(string, Stream)>> bossImageTask = assaultData.List.Select(async x =>
            {
                AssaultBoss boss = x.Boss[0];
                Stream stream = new MemoryStream();
                await m_ApiService.GetBossImageAsync(boss, stream);
                return (boss.Name, stream);
            });
            IEnumerable<Task<(string, Stream)>> buffImageTask = assaultData.List.Select(async x =>
            {
                AssaultBuff buff = x.Buff[0];
                Stream stream = new MemoryStream();
                await m_ApiService.GetBuffImageAsync(buff, stream);
                return (buff.Name, stream);
            });

            await Task.WhenAll(avatarImageTask.Concat(buddyImageTask));
            await Task.WhenAll(bossImageTask);
            await Task.WhenAll(buffImageTask);

            Dictionary<string, Stream> bossImages = bossImageTask.ToDictionary(x => x.Result.Item1, x => x.Result.Item2);
            Dictionary<string, Stream> buffImages = buffImageTask.ToDictionary(x => x.Result.Item1, x => x.Result.Item2);

            InteractionMessageProperties message = await GetMessageAsync(assaultData, response.Data, server, bossImages, buffImages);
            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                .AddComponents(new TextDisplayProperties("Command execution completed")));
            await Context.Interaction.SendFollowupMessageAsync(message);
            BotMetrics.TrackCommand(Context.Interaction.User, "zzz da", true);
        }
        catch (CommandException e)
        {
            BotMetrics.TrackCommand(Context.Interaction.User, "zzz da", false);
            Logger.LogError(e, "Error fetching Zzz Assault data for user {UserId}", Context.Interaction.User.Id);
            await SendErrorMessageAsync(e.Message, false);
        }
        catch (Exception ex)
        {
            BotMetrics.TrackCommand(Context.Interaction.User, "zzz da", false);
            Logger.LogError(ex, "Unexpected error fetching Zzz Assault data for user {UserId}", Context.Interaction.User.Id);
            await SendErrorMessageAsync(followup: false);
        }
    }

    private async ValueTask<InteractionMessageProperties> GetMessageAsync(ZzzAssaultData data, UserGameData gameData,
        Regions server, Dictionary<string, Stream> bossImages, Dictionary<string, Stream> buffImages)
    {
        InteractionMessageProperties message = new();
        TimeZoneInfo tz = server.GetTimeZoneInfo();
        message.WithFlags(MessageFlags.IsComponentsV2);
        ComponentContainerProperties container =
            [
                new TextDisplayProperties(
                    $"### <@{Context.Interaction.User.Id}>'s Deadly Assault Summary"),
                new TextDisplayProperties(
                    $"Cycle start: <t:{data.StartTime.ToTimestamp(tz)}:f>\nCycle end: <t:{data.EndTime.ToTimestamp(tz)}:f>"),
                new MediaGalleryProperties().AddItems(
                    new MediaGalleryItemProperties(new ComponentMediaProperties($"attachment://assault_card.jpg"))),
                new TextDisplayProperties(
                    $"-# Information may be inaccurate due to API limitations. Please check in-game for the most accurate data.")
            ];
        message.WithComponents([container]);
        message.AddAttachments(new AttachmentProperties($"assault_card.jpg",
            await m_CommandService.GetAssaultCardAsync(data, gameData, bossImages, buffImages)));

        return message;
    }
}
