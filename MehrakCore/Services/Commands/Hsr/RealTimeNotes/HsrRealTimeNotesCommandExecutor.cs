#region

using Mehrak.Domain.Interfaces;
using Mehrak.Domain.Utility;
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

namespace MehrakCore.Services.Commands.Hsr.RealTimeNotes;

public class HsrRealTimeNotesCommandExecutor : BaseCommandExecutor<HsrRealTimeNotesCommandExecutor>,
    IRealTimeNotesCommandExecutor<HsrCommandModule>
{
    private readonly IRealTimeNotesApiService<HsrRealTimeNotesData> m_ApiService;
    private readonly ImageRepository m_ImageRepository;

    private Server m_PendingServer;

    public HsrRealTimeNotesCommandExecutor(IRealTimeNotesApiService<HsrRealTimeNotesData> apiService,
        UserRepository userRepository, ImageRepository imageRepository, RedisCacheService tokenCacheService,
        IAuthenticationMiddlewareService authenticationMiddleware, GameRecordApiService gameRecordApi,
        ILogger<HsrRealTimeNotesCommandExecutor> logger)
        : base(userRepository, tokenCacheService, authenticationMiddleware, gameRecordApi, logger)
    {
        m_ApiService = apiService;
        m_ImageRepository = imageRepository;
    }

    public override async ValueTask ExecuteAsync(params object?[] parameters)
    {
        if (parameters.Length != 2)
            throw new ArgumentException("Invalid parameters count for real-time notes command");

        var server = (Server?)parameters[0];
        var profile = parameters[1] == null ? 1 : (uint)parameters[1]!;

        try
        {
            var (user, selectedProfile) = await ValidateUserAndProfileAsync(profile);
            if (user == null || selectedProfile == null)
                return;

            // Auto-select server from cache if not provided
            if (selectedProfile.LastUsedRegions != null && !server.HasValue &&
                selectedProfile.LastUsedRegions.TryGetValue(Game.HonkaiStarRail, out var tmp))
                server = tmp;

            var cachedServer = server ?? GetCachedServer(selectedProfile, Game.HonkaiStarRail);
            if (!await ValidateServerAsync(cachedServer))
                return;
            m_PendingServer = cachedServer!.Value;
            var ltoken = await GetOrRequestAuthenticationAsync(selectedProfile, profile);

            if (ltoken != null)
            {
                Logger.LogInformation("User {UserId} is already authenticated", Context.Interaction.User.Id);
                await Context.Interaction.SendResponseAsync(
                    InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
                await SendRealTimeNotesAsync(selectedProfile.LtUid, ltoken, cachedServer.Value);
            }
        }
        catch (CommandException e)
        {
            Logger.LogError(e, "Error executing real-time notes command for user {UserId}",
                Context.Interaction.User.Id);
            await SendErrorMessageAsync(e.Message);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error executing real-time notes command for user {UserId}",
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
            await SendRealTimeNotesAsync(result.LtUid, result.LToken, m_PendingServer);
        }
        else
        {
            Logger.LogWarning("Authentication failed for user {UserId}: {ErrorMessage}",
                Context.Interaction.User.Id, result.ErrorMessage);
        }
    }

    private async ValueTask SendRealTimeNotesAsync(ulong ltuid, string ltoken, Server server)
    {
        try
        {
            var region = server.GetRegion();
            var user = await UserRepository.GetUserAsync(Context.Interaction.User.Id);

            var result = await GetAndUpdateGameDataAsync(user, Game.HonkaiStarRail, ltuid, ltoken, server,
                server.GetRegion());

            if (!result.IsSuccess) return;

            var gameUid = result.Data.GameUid!;
            var notesResult = await m_ApiService.GetRealTimeNotesAsync(gameUid, region, ltuid, ltoken);

            if (!notesResult.IsSuccess)
            {
                Logger.LogError("Failed to fetch real-time notes for user {UserId}: {ErrorMessage}",
                    Context.Interaction.User.Id, notesResult.ErrorMessage);
                await SendErrorMessageAsync(notesResult.ErrorMessage);
                BotMetrics.TrackCommand(Context.Interaction.User, "hsr notes", false);
                return;
            }

            var notesData = notesResult.Data;
            await Context.Interaction.SendFollowupMessageAsync(await BuildRealTimeNotes(notesData, server, gameUid));
            Logger.LogInformation("Successfully fetched real-time notes for user {UserId} in region {Region}",
                Context.Interaction.User.Id, region);
            BotMetrics.TrackCommand(Context.Interaction.User, "hsr notes", true);
        }
        catch (CommandException e)
        {
            Logger.LogError("Error processing real-time notes command for user {UserId}: {ErrorMessage}",
                Context.Interaction.User.Id, e.Message);
            await SendErrorMessageAsync(e.Message);
            BotMetrics.TrackCommand(Context.Interaction.User, "hsr notes", false);
        }
        catch (Exception e)
        {
            Logger.LogError(
                "Error sending real-time notes response with for user {UserId}: {ErrorMessage}",
                Context.Interaction.User.Id, e.Message);
            await SendErrorMessageAsync();
            BotMetrics.TrackCommand(Context.Interaction.User, "hsr notes", false);
        }
    }

    private async ValueTask<InteractionMessageProperties> BuildRealTimeNotes(HsrRealTimeNotesData data, Server region,
        string uid)
    {
        try
        {
            var tbpImage = m_ImageRepository.DownloadFileToStreamAsync("hsr_tbp");
            var assignmentImage = m_ImageRepository.DownloadFileToStreamAsync("hsr_assignment");
            var weeklyImage = m_ImageRepository.DownloadFileToStreamAsync("hsr_weekly");
            var rogueImage = m_ImageRepository.DownloadFileToStreamAsync("hsr_rogue");

            var weeklyReset = region.GetNextWeeklyResetUnix();
            InteractionMessageProperties result = new();
            result.WithFlags(MessageFlags.IsComponentsV2 | MessageFlags.Ephemeral);
            var container = new ComponentContainerProperties();
            result.AddComponents([container]);
            container.AddComponents(new TextDisplayProperties($"## HSR Real-Time Notes (UID: {uid})"));
            container.AddComponents(
                new ComponentSectionProperties(
                        new ComponentSectionThumbnailProperties(
                            new ComponentMediaProperties("attachment://hsr_tbp.png")))
                    .WithComponents([
                        new TextDisplayProperties("### Trailblaze Power"),
                        new TextDisplayProperties($"{data.CurrentStamina}/{data.MaxStamina}"),
                        new TextDisplayProperties(data.CurrentStamina == data.MaxStamina
                            ? "-# Already Full!"
                            : $"-# Recovers <t:{data.StaminaFullTs}:R>")
                    ]),
                new ComponentSectionProperties(
                        new ComponentSectionThumbnailProperties(
                            new ComponentMediaProperties("attachment://hsr_assignment.png")))
                    .WithComponents([
                        new TextDisplayProperties("### Assignments"),
                        new TextDisplayProperties(data.AcceptedExpeditionNum > 0
                            ? $"{data.AcceptedExpeditionNum}/{data.MaxStamina}"
                            : "None Accepted!"),
                        new TextDisplayProperties(data.AcceptedExpeditionNum > 0
                            ? data.Expeditions!.Max(x => x.FinishTs) > DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                                ? $"-# Completes <t:{data.Expeditions!.Max(x => x.FinishTs)}:R>"
                                : "-# All Assignments Completed!"
                            : "-# To be dispatched")
                    ]),
                new ComponentSectionProperties(
                        new ComponentSectionThumbnailProperties(
                            new ComponentMediaProperties("attachment://hsr_weekly.png")))
                    .WithComponents([
                        new TextDisplayProperties("### Echoes of War"),
                        new TextDisplayProperties(data.WeeklyCocoonCnt > 0
                            ? $"Claimed {data.WeeklyCocoonLimit - data.WeeklyCocoonCnt}/{data.WeeklyCocoonLimit}"
                            : "Fully Claimed!"),
                        new TextDisplayProperties($"-# Resets <t:{weeklyReset}:R>")
                    ]),
                new ComponentSectionProperties(
                        new ComponentSectionThumbnailProperties(
                            new ComponentMediaProperties("attachment://hsr_rogue.png")))
                    .WithComponents([
                        new TextDisplayProperties("### Simulated Universe"),
                        new TextDisplayProperties($"{data.CurrentRogueScore}/{data.MaxRogueScore}"),
                        new TextDisplayProperties($"-# Resets <t:{weeklyReset}:R>")
                    ])
            );
            result.AddAttachments(new AttachmentProperties("hsr_tbp.png", await tbpImage),
                new AttachmentProperties("hsr_assignment.png", await assignmentImage),
                new AttachmentProperties("hsr_weekly.png", await weeklyImage),
                new AttachmentProperties("hsr_rogue.png", await rogueImage));
            return result;
        }
        catch (CommandException)
        {
            throw;
        }
        catch (Exception e)
        {
            throw new CommandException("An error occurred while generating real-time notes response", e);
        }
    }
}
