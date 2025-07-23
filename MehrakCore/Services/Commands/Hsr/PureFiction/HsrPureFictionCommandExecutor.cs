#region

using MehrakCore.ApiResponseTypes;
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

namespace MehrakCore.Services.Commands.Hsr.PureFiction;

public class HsrPureFictionCommandExecutor : BaseCommandExecutor<HsrCommandModule>
{
    private readonly ImageUpdaterService<HsrCharacterInformation> m_ImageUpdaterService;
    private readonly HsrPureFictionCardService m_CommandService;
    private readonly HsrPureFictionApiService m_ApiService;

    private Regions m_PendingServer;

    public HsrPureFictionCommandExecutor(ICommandService<HsrPureFictionCommandExecutor> commandService,
        IApiService<HsrPureFictionCommandExecutor> apiService,
        ImageUpdaterService<HsrCharacterInformation> imageUpdaterService,
        UserRepository userRepository, TokenCacheService tokenCacheService,
        IAuthenticationMiddlewareService authenticationMiddleware, GameRecordApiService gameRecordApi,
        ILogger<HsrCommandModule> logger) : base(userRepository, tokenCacheService, authenticationMiddleware,
        gameRecordApi, logger)
    {
        m_ImageUpdaterService = imageUpdaterService;
        m_CommandService = (HsrPureFictionCardService)commandService;
        m_ApiService = (HsrPureFictionApiService)apiService;
    }

    public override async ValueTask ExecuteAsync(params object?[] parameters)
    {
        if (parameters.Length != 2)
            throw new ArgumentException("Invalid number of parameters provided.");

        var server = (Regions?)parameters[0];
        var profile = (uint)(parameters[1] ?? 1);

        try
        {
            var (user, selectedProfile) = await ValidateUserAndProfileAsync(profile);
            if (user == null || selectedProfile == null) return;

            server ??= GetCachedServer(selectedProfile, GameName.HonkaiStarRail);
            if (server == null)
            {
                await SendErrorMessageAsync("No cached server found! Please select a server first.", false);
                return;
            }

            m_PendingServer = server.Value;

            var ltoken = await GetOrRequestAuthenticationAsync(selectedProfile, profile);
            if (ltoken != null)
            {
                await Context.Interaction.SendResponseAsync(
                    InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
                await SendPureFictionCardAsync(server.Value, selectedProfile.LtUid, ltoken).ConfigureAwait(false);
            }
        }
        catch (CommandException e)
        {
            Logger.LogError(e, "Error processing Memory command for user {UserId} profile {Profile}",
                Context.Interaction.User.Id, profile);
            await SendErrorMessageAsync(e.Message, false);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing Memory command for user {UserId} profile {Profile}",
                Context.Interaction.User.Id, profile);
            await SendErrorMessageAsync(followup: false);
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
        await SendPureFictionCardAsync(m_PendingServer, result.LtUid, result.LToken).ConfigureAwait(false);
    }

    private async ValueTask SendPureFictionCardAsync(Regions server, ulong ltuid, string ltoken)
    {
        try
        {
            var region = server.GetRegion();
            var user = await UserRepository.GetUserAsync(Context.Interaction.User.Id);
            var response =
                await GetAndUpdateGameDataAsync(user, GameName.HonkaiStarRail, ltuid, ltoken, server, region);
            if (!response.IsSuccess)
                return;

            var gameData = response.Data;
            var pureFictionResponse =
                await m_ApiService.GetPureFictionDataAsync(gameData.GameUid!, region, ltuid, ltoken);
            if (!pureFictionResponse.IsSuccess)
            {
                Logger.LogError("Failed to fetch Pure Fiction data for user {UserId}: {ErrorMessage}",
                    Context.Interaction.User.Id, pureFictionResponse.ErrorMessage);
                await SendErrorMessageAsync(pureFictionResponse.ErrorMessage);
                return;
            }

            var pureFictionData = pureFictionResponse.Data;
            if (!pureFictionData.HasData)
            {
                Logger.LogInformation("No Pure Fiction clear records found for user {UserId}",
                    Context.Interaction.User.Id);
                await SendErrorMessageAsync("No Pure Fiction clear records found");
                return;
            }

            var tasks = pureFictionData.AllFloorDetail!.SelectMany(x => x.Node1!.Avatars.Concat(x.Node2!.Avatars))
                .DistinctBy(x => x.Id)
                .Select(async x =>
                    await m_ImageUpdaterService.UpdateAvatarAsync(x.Id.ToString(), x.Icon));
            await Task.WhenAll(tasks);

            var buffMap = await m_ApiService.GetBuffMapAsync(pureFictionData);

            var message = await GetFictionCardAsync(response.Data, pureFictionData, server, buffMap);
            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                .AddComponents(new TextDisplayProperties("Command execution completed")));
            await Context.Interaction.SendFollowupMessageAsync(message);
            BotMetrics.TrackCommand(Context.Interaction.User, "hsr pf", true);
        }
        catch (CommandException e)
        {
            Logger.LogError(e, "Error processing Pure Fiction card for user {UserId}",
                Context.Interaction.User.Id);
            await SendErrorMessageAsync(e.Message);
            BotMetrics.TrackCommand(Context.Interaction.User, "hsr pf", true);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error processing Pure Fiction card for user {UserId}",
                Context.Interaction.User.Id);
            await SendErrorMessageAsync("An error occurred while generating Pure Fiction card");
            BotMetrics.TrackCommand(Context.Interaction.User, "hsr pf", true);
        }
    }

    private async ValueTask<InteractionMessageProperties> GetFictionCardAsync(UserGameData gameData,
        HsrPureFictionInformation fictionData, Regions region, Dictionary<string, Stream> buffMap)
    {
        try
        {
            var tz = region.GetTimeZoneInfo();
            var group = fictionData.Groups.First();
            var startTime = new DateTimeOffset(group.BeginTime.ToDateTime(), tz.BaseUtcOffset)
                .ToUnixTimeSeconds();
            var endTime = new DateTimeOffset(group.EndTime.ToDateTime(), tz.BaseUtcOffset)
                .ToUnixTimeSeconds();
            InteractionMessageProperties message = new();
            ComponentContainerProperties container =
            [
                new TextDisplayProperties(
                    $"### <@{Context.Interaction.User.Id}>'s Pure Fiction Summary"),
                new TextDisplayProperties(
                    $"Cycle start: <t:{startTime}:f>\nCycle end: <t:{endTime}:f>"),
                new MediaGalleryProperties().AddItems(
                    new MediaGalleryItemProperties(new ComponentMediaProperties("attachment://pf_card.jpg"))),
                new TextDisplayProperties(
                    $"-# Information may be inaccurate due to API limitations. Please check in-game for the most accurate data.")
            ];

            message.WithComponents([container]);
            message.WithFlags(MessageFlags.IsComponentsV2);
            message.AddAttachments(new AttachmentProperties("pf_card.jpg",
                await m_CommandService.GetFictionCardImageAsync(gameData, fictionData, buffMap)));

            return message;
        }
        catch (CommandException)
        {
            throw;
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error generating Pure Fiction card for user {UserId}",
                Context.Interaction.User.Id);
            throw new CommandException("An error occurred while generating Pure Fiction card", e);
        }
    }
}
