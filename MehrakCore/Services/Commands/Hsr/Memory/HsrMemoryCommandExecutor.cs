#region

using MehrakCore.ApiResponseTypes;
using MehrakCore.ApiResponseTypes.Hsr;
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

namespace MehrakCore.Services.Commands.Hsr.Memory;

public class HsrMemoryCommandExecutor : BaseCommandExecutor<HsrCommandModule>
{
    private readonly HsrMemoryApiService m_ApiService;
    private readonly ImageUpdaterService<HsrCharacterInformation> m_ImageUpdaterService;
    private readonly HsrMemoryCardService m_CommandService;

    private Regions m_PendingServer;

    public HsrMemoryCommandExecutor(IApiService<HsrMemoryCommandExecutor> apiService,
        ImageUpdaterService<HsrCharacterInformation> imageUpdaterService,
        ICommandService<HsrMemoryCommandExecutor> commandService,
        UserRepository userRepository, TokenCacheService tokenCacheService,
        IAuthenticationMiddlewareService authenticationMiddleware, GameRecordApiService gameRecordApi,
        ILogger<HsrCommandModule> logger) : base(userRepository, tokenCacheService, authenticationMiddleware,
        gameRecordApi, logger)
    {
        m_ApiService = (HsrMemoryApiService)apiService;
        m_ImageUpdaterService = imageUpdaterService;
        m_CommandService = (HsrMemoryCardService)commandService;
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
                await SendMemoryCardAsync(server.Value, selectedProfile.LtUid, ltoken).ConfigureAwait(false);
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
            Logger.LogWarning("Authentication failed for user {UserId}: {ErrorMessage}", Context.Interaction.User.Id,
                result.ErrorMessage);
            return;
        }

        Context = result.Context;
        Logger.LogInformation("Authentication completed successfully for user {UserId}",
            Context.Interaction.User.Id);
        await SendMemoryCardAsync(m_PendingServer, result.LtUid, result.LToken).ConfigureAwait(false);
    }

    private async ValueTask SendMemoryCardAsync(Regions server, ulong ltuid, string ltoken)
    {
        try
        {
            var region = server.GetRegion();
            var user = await UserRepository.GetUserAsync(Context.Interaction.User.Id);
            var response =
                await GetAndUpdateGameDataAsync(user, GameName.HonkaiStarRail, ltuid, ltoken, server, region);
            if (!response.IsSuccess)
                return;

            var gameUid = response.Data.GameUid!;
            var memoryResult = await m_ApiService.GetMemoryInformationAsync(gameUid, region, ltuid, ltoken);
            if (!memoryResult.IsSuccess)
            {
                Logger.LogWarning(
                    "Failed to fetch Memory of Chaos information for gameUid: {GameUid}, region: {Region}, error: {Error}",
                    gameUid, region, memoryResult.ErrorMessage);
                await SendErrorMessageAsync(memoryResult.ErrorMessage);
                return;
            }

            var memoryData = memoryResult.Data;

            if (!memoryData.HasData || memoryData.BattleNum == 0)
            {
                Logger.LogInformation(
                    "No Memory of Chaos data found for user {UserId} in region {Region}", Context.Interaction.User.Id,
                    region);
                await SendErrorMessageAsync("No clear record found");
                return;
            }

            var tasks = memoryData.AllFloorDetail!.SelectMany(x => x.Node1.Avatars.Concat(x.Node2.Avatars))
                .DistinctBy(x => x.Id)
                .Select(async x =>
                    await m_ImageUpdaterService.UpdateAvatarAsync(x.Id.ToString(), x.Icon));
            await Task.WhenAll(tasks);

            var message = await GetMemoryCardAsync(response.Data, memoryData, server);
            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                .AddComponents(new TextDisplayProperties("Command execution completed")));
            await Context.Interaction.SendFollowupMessageAsync(message);
            BotMetrics.TrackCommand(Context.Interaction.User, "hsr moc", true);
        }
        catch (CommandException e)
        {
            Logger.LogError(e, "Error processing Memory card for user {UserId}",
                Context.Interaction.User.Id);
            await SendErrorMessageAsync(e.Message);
            BotMetrics.TrackCommand(Context.Interaction.User, "hsr moc", true);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error processing Memory card for user {UserId}",
                Context.Interaction.User.Id);
            await SendErrorMessageAsync("An error occurred while generating Memory of Chaos card");
            BotMetrics.TrackCommand(Context.Interaction.User, "hsr moc", true);
        }
    }

    private async ValueTask<InteractionMessageProperties> GetMemoryCardAsync(UserGameData gameData,
        HsrMemoryInformation memoryData, Regions region)
    {
        try
        {
            var tz = region.GetTimeZoneInfo();
            var startTime = new DateTimeOffset(memoryData.StartTime.ToDateTime(), tz.BaseUtcOffset)
                .ToUnixTimeSeconds();
            var endTime = new DateTimeOffset(memoryData.EndTime.ToDateTime(), tz.BaseUtcOffset)
                .ToUnixTimeSeconds();
            InteractionMessageProperties message = new();
            ComponentContainerProperties container =
            [
                new TextDisplayProperties(
                    $"### <@{Context.Interaction.User.Id}>'s Memory of Chaos Summary"),
                new TextDisplayProperties(
                    $"Cycle start: <t:{startTime}:f>\nCycle end: <t:{endTime}:f>"),
                new MediaGalleryProperties().AddItems(
                    new MediaGalleryItemProperties(new ComponentMediaProperties("attachment://moc_card.jpg"))),
                new TextDisplayProperties(
                    $"-# Information may be inaccurate due to API limitations. Please check in-game for the most accurate data.")
            ];

            message.WithComponents([container]);
            message.WithFlags(MessageFlags.IsComponentsV2);
            message.AddAttachments(new AttachmentProperties("moc_card.jpg",
                await m_CommandService.GetMemoryCardImageAsync(gameData, memoryData)));

            return message;
        }
        catch (CommandException)
        {
            throw;
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error generating Memory card for user {UserId}",
                Context.Interaction.User.Id);
            throw new CommandException("An error occurred while generating Memory of Chaos card", e);
        }
    }
}
