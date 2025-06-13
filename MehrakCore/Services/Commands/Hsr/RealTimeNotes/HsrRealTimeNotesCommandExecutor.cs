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

namespace MehrakCore.Services.Commands.Hsr.RealTimeNotes;

public class HsrRealTimeNotesCommandExecutor : BaseCommandExecutor<HsrRealTimeNotesCommandExecutor>,
    IRealTimeNotesCommandExecutor<HsrCommandModule>
{
    private readonly IRealTimeNotesApiService<HsrRealTimeNotesData> m_ApiService;
    private readonly ImageRepository m_ImageRepository;
    private readonly GameRecordApiService m_GameRecordApi;

    private Regions m_PendingServer;

    public HsrRealTimeNotesCommandExecutor(IRealTimeNotesApiService<HsrRealTimeNotesData> apiService,
        UserRepository userRepository, ImageRepository imageRepository, TokenCacheService tokenCacheService,
        IAuthenticationMiddlewareService authenticationMiddleware, GameRecordApiService gameRecordApi,
        ILogger<HsrRealTimeNotesCommandExecutor> logger)
        : base(userRepository, tokenCacheService, authenticationMiddleware, logger)
    {
        m_ApiService = apiService;
        m_ImageRepository = imageRepository;
        m_GameRecordApi = gameRecordApi;
    }

    public override async ValueTask ExecuteAsync(params object?[] parameters)
    {
        if (parameters.Length != 2)
            throw new ArgumentException("Invalid parameters count for real-time notes command");

        var server = (Regions?)parameters[0];
        var profile = parameters[1] == null ? 1 : (uint)parameters[1]!;

        try
        {
            var (user, selectedProfile) = await ValidateUserAndProfileAsync(profile);
            if (user == null || selectedProfile == null)
                return;

            // Auto-select server from cache if not provided
            if (selectedProfile.LastUsedRegions != null && !server.HasValue &&
                selectedProfile.LastUsedRegions.TryGetValue(GameName.HonkaiStarRail, out var tmp))
                server = tmp;

            var cachedServer = server ?? GetCachedServer(selectedProfile, GameName.HonkaiStarRail);
            if (!await ValidateServerAsync(cachedServer))
                return;

            var ltoken = await GetOrRequestAuthenticationAsync(selectedProfile, profile,
                () => { m_PendingServer = cachedServer!.Value; });

            if (ltoken != null)
            {
                Logger.LogInformation("User {UserId} is already authenticated", Context.Interaction.User.Id);
                await Context.Interaction.SendResponseAsync(
                    InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
                await SendRealTimeNotesAsync(selectedProfile.LtUid, ltoken, cachedServer!.Value);
            }
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error executing real-time notes command for user {UserId}",
                Context.Interaction.User.Id);
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                new InteractionMessageProperties().WithContent("An error occurred while processing your request")
                    .WithFlags(MessageFlags.Ephemeral)));
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
            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .AddComponents(new TextDisplayProperties($"Authentication failed: {result.ErrorMessage}"))
                .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2));
        }
    }

    private async ValueTask SendRealTimeNotesAsync(ulong ltuid, string ltoken, Regions server)
    {
        try
        {
            var region = RegionUtility.GetRegion(server);
            var user = await UserRepository.GetUserAsync(Context.Interaction.User.Id);

            var selectedProfile = user?.Profiles?.FirstOrDefault(x => x.LtUid == ltuid);

            // edge case check that probably will never occur
            // but if user removes their profile while this command is running will result in null
            if (user?.Profiles == null || selectedProfile == null)
            {
                Logger.LogDebug("User {UserId} does not have a profile with ltuid {LtUid}",
                    Context.Interaction.User.Id, ltuid);
                await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                    .WithFlags(MessageFlags.IsComponentsV2).WithComponents([
                        new TextDisplayProperties("No profile found. Please select the correct profile")
                    ]));
                return;
            }

            if (selectedProfile.GameUids == null ||
                !selectedProfile.GameUids.TryGetValue(GameName.HonkaiStarRail, out var dict) ||
                !dict.TryGetValue(server.ToString(), out var gameUid))
            {
                Logger.LogDebug("User {UserId} does not have a game UID for region {Region}",
                    Context.Interaction.User.Id, region);
                var result = await m_GameRecordApi.GetUserRegionUidAsync(ltuid, ltoken, "hkrpg_global", region);
                if (result.RetCode == -100)
                {
                    await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                        .WithFlags(MessageFlags.IsComponentsV2).WithComponents([
                            new TextDisplayProperties("Invalid HoYoLAB UID or Cookies. Please authenticate again.")
                        ]));
                    return;
                }

                gameUid = result.Data;
            }

            if (gameUid == null)
            {
                await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                    .WithFlags(MessageFlags.IsComponentsV2).WithComponents([
                        new TextDisplayProperties("No game information found. Please select the correct region")
                    ]));
                return;
            }

            selectedProfile.GameUids ??= new Dictionary<GameName, Dictionary<string, string>>();

            if (!selectedProfile.GameUids.ContainsKey(GameName.HonkaiStarRail))
                selectedProfile.GameUids[GameName.HonkaiStarRail] = new Dictionary<string, string>();
            if (!selectedProfile.GameUids[GameName.HonkaiStarRail].TryAdd(server.ToString(), gameUid))
                selectedProfile.GameUids[GameName.HonkaiStarRail][server.ToString()] = gameUid;
            Logger.LogDebug("Found game UID {GameUid} for User {UserId} in region {Region}", gameUid,
                Context.Interaction.User.Id, region);

            selectedProfile.LastUsedRegions ??= new Dictionary<GameName, Regions>();

            if (!selectedProfile.LastUsedRegions.TryAdd(GameName.HonkaiStarRail, server))
                selectedProfile.LastUsedRegions[GameName.HonkaiStarRail] = server;

            var updateUser = UserRepository.CreateOrUpdateUserAsync(user);
            var realTimeNotes = m_ApiService.GetRealTimeNotesAsync(gameUid, region, ltuid, ltoken);
            await Task.WhenAll(updateUser, realTimeNotes);

            var notesResult = await realTimeNotes;
            if (!notesResult.IsSuccess)
            {
                Logger.LogError("Failed to fetch real-time notes for user {UserId}: {ErrorMessage}",
                    Context.Interaction.User.Id, notesResult.ErrorMessage);
                await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                    .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                    .AddComponents(new TextDisplayProperties($"An error occurred: {notesResult.ErrorMessage}")));
                BotMetrics.TrackCommand(Context.Interaction.User, "hsr notes", false);
                return;
            }

            var notesData = notesResult.Data;
            await Context.Interaction.SendFollowupMessageAsync(await BuildRealTimeNotes(notesData, server, gameUid));
            Logger.LogInformation("Successfully fetched real-time notes for user {UserId} in region {Region}",
                Context.Interaction.User.Id, region);
            BotMetrics.TrackCommand(Context.Interaction.User, "hsr notes", true);
        }
        catch (Exception e)
        {
            Logger.LogError(
                "Error sending real-time notes response with for user {UserId}: {ErrorMessage}",
                Context.Interaction.User.Id, e.Message);
            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                .AddComponents(new TextDisplayProperties($"An unknown error occurred, please try again later.")));
            BotMetrics.TrackCommand(Context.Interaction.User, "hsr notes", false);
        }
    }

    private async ValueTask<InteractionMessageProperties> BuildRealTimeNotes(HsrRealTimeNotesData data, Regions region,
        string uid)
    {
        var tbpImage = m_ImageRepository.DownloadFileToStreamAsync("hsr_tbp");
        var assignmentImage = m_ImageRepository.DownloadFileToStreamAsync("hsr_assignment");
        var weeklyImage = m_ImageRepository.DownloadFileToStreamAsync("hsr_weekly");
        var rogueImage = m_ImageRepository.DownloadFileToStreamAsync("hsr_rogue");

        var weeklyReset = GetNextWeeklyResetUnix(region);
        InteractionMessageProperties result = new();
        result.WithFlags(MessageFlags.IsComponentsV2 | MessageFlags.Ephemeral);
        var container = new ComponentContainerProperties();
        result.AddComponents([container]);
        container.AddComponents(new TextDisplayProperties($"## HSR Real-Time Notes (UID: {uid})"));
        container.AddComponents(
            new ComponentSectionProperties(
                    new ComponentSectionThumbnailProperties(new ComponentMediaProperties("attachment://hsr_tbp.png")))
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
                        ? $"-# Completes <t:{data.Expeditions!.Max(x => x.FinishTs)}:R>"
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
                    new ComponentSectionThumbnailProperties(new ComponentMediaProperties("attachment://hsr_rogue.png")))
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

    private static long GetNextWeeklyResetUnix(Regions region)
    {
        var tz = region.GetTimeZoneInfo();
        var nowUtc = DateTime.UtcNow;
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz);

        // Calculate days until next Monday
        var daysUntilMonday = ((int)DayOfWeek.Monday - (int)nowLocal.DayOfWeek + 7) % 7;
        if (daysUntilMonday == 0 && nowLocal.TimeOfDay >= TimeSpan.FromHours(4))
            daysUntilMonday = 7; // If it's already Monday after 4AM, go to next week

        var nextMondayLocal = nowLocal.Date.AddDays(daysUntilMonday).AddHours(4);

        // Convert back to UTC
        return new DateTimeOffset(nextMondayLocal).ToUnixTimeSeconds();
    }
}
