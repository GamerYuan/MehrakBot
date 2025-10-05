using MehrakCore.ApiResponseTypes.Zzz;
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

namespace MehrakCore.Services.Commands.Zzz.RealTimeNotes;

public class ZzzRealTimeNotesCommandExecutor : BaseCommandExecutor<ZzzRealTimeNotesCommandExecutor>,
    IRealTimeNotesCommandExecutor<ZzzCommandModule>
{
    private Regions m_PendingServer;
    private readonly ImageRepository m_ImageRepository;
    public readonly IRealTimeNotesApiService<ZzzRealTimeNotesData> m_ApiService;

    public ZzzRealTimeNotesCommandExecutor(
        IRealTimeNotesApiService<ZzzRealTimeNotesData> apiService,
        ImageRepository imageRepository,
        UserRepository userRepository,
        TokenCacheService tokenCacheService,
        IAuthenticationMiddlewareService authenticationMiddleware,
        GameRecordApiService gameRecordApi,
        ILogger<ZzzRealTimeNotesCommandExecutor> logger) :
        base(userRepository, tokenCacheService, authenticationMiddleware, gameRecordApi, logger)
    {
        m_ApiService = apiService;
        m_ImageRepository = imageRepository;
    }

    public override async ValueTask ExecuteAsync(params object?[] parameters)
    {
        if (parameters.Length != 2)
            throw new ArgumentException("Invalid parameters count for real-time notes command");

        Regions? server = (Regions?)parameters[0];
        uint profile = parameters[1] == null ? 1 : (uint)parameters[1]!;

        try
        {
            (UserModel? user, UserProfile? selectedProfile) = await ValidateUserAndProfileAsync(profile);
            if (user == null || selectedProfile == null)
                return;

            // Auto-select server from cache if not provided
            if (selectedProfile.LastUsedRegions != null && !server.HasValue &&
                selectedProfile.LastUsedRegions.TryGetValue(GameName.ZenlessZoneZero, out Regions tmp))
                server = tmp;

            Regions? cachedServer = server ?? GetCachedServer(selectedProfile, GameName.ZenlessZoneZero);
            if (!await ValidateServerAsync(cachedServer))
                return;

            m_PendingServer = cachedServer!.Value;
            string? ltoken = await GetOrRequestAuthenticationAsync(selectedProfile, profile);

            if (ltoken != null)
            {
                Logger.LogInformation("User {UserId} is already authenticated", Context.Interaction.User.Id);
                await Context.Interaction.SendResponseAsync(
                    InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
                await SendRealTimeNotesAsync(selectedProfile.LtUid, ltoken, cachedServer.Value);
            }
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

    public async Task SendRealTimeNotesAsync(ulong ltuid, string ltoken, Regions server)
    {
        try
        {
            string region = server.GetRegion();
            UserModel? user = await UserRepository.GetUserAsync(Context.Interaction.User.Id);

            ApiResult<ApiResponseTypes.UserGameData> result = await GetAndUpdateGameDataAsync(user, GameName.ZenlessZoneZero, ltuid, ltoken, server,
                server.GetRegion());

            if (!result.IsSuccess) return;

            string gameUid = result.Data.GameUid!;
            ApiResult<ZzzRealTimeNotesData> notesResult = await m_ApiService.GetRealTimeNotesAsync(gameUid, region, ltuid, ltoken);

            if (!notesResult.IsSuccess)
            {
                Logger.LogError("Failed to fetch real-time notes: {ErrorMessage}", notesResult.ErrorMessage);
                await SendErrorMessageAsync(notesResult.ErrorMessage);
                return;
            }

            ZzzRealTimeNotesData notesData = notesResult.Data;
            if (notesData == null)
            {
                Logger.LogError("No data found in real-time notes response");
                await SendErrorMessageAsync("No data found in real-time notes response");
                return;
            }

            await Context.Interaction.SendFollowupMessageAsync(await BuildRealTimeNotes(notesData, server, gameUid));
            Logger.LogInformation("Successfully fetched real-time notes for user {UserId} in region {Region}",
                Context.Interaction.User.Id, region);
            BotMetrics.TrackCommand(Context.Interaction.User, "zzz notes", true);
        }
        catch (CommandException e)
        {
            Logger.LogError(e, "Error fetching real-time notes for user {UserId} in region {Region}",
                Context.Interaction.User.Id, server);
            await SendErrorMessageAsync(e.Message);
            BotMetrics.TrackCommand(Context.Interaction.User, "zzz notes", false);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error fetching real-time notes for user {UserId} in region {Region}",
                Context.Interaction.User.Id, server);
            await SendErrorMessageAsync();
            BotMetrics.TrackCommand(Context.Interaction.User, "zzz notes", false);
        }
    }

    public async Task<InteractionMessageProperties> BuildRealTimeNotes(ZzzRealTimeNotesData data, Regions server, string gameUid)
    {
        Stream stamImage = await m_ImageRepository.DownloadFileToStreamAsync("zzz_battery");

        long currTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        InteractionMessageProperties response = new();
        response.WithFlags(MessageFlags.IsComponentsV2 | MessageFlags.Ephemeral);

        string omnicoins = data.TempleManage.CurrencyNextRefreshTs == 0
            ? "-"
            : $"{data.TempleManage.CurrentCurrency}/{data.TempleManage.WeeklyCurrencyMax}" +
              $"-# Refreshes in <t:{data.TempleManage.CurrencyNextRefreshTs}:F>";
        string bounty = data.BountyCommission.Total == 0
            ? "-"
            : $"{data.BountyCommission.Num}/{data.BountyCommission.Total}\n" +
              $"-# Refreshes in <t:{currTime + data.BountyCommission.RefreshTime}:F>";
        string weeklyTask = data.WeeklyTask is null
            ? "-"
            : $"{data.WeeklyTask!.CurPoint}/{data.WeeklyTask!.MaxPoint}\n" +
              $"-# Refreshes in <t:{currTime + data.WeeklyTask.RefreshTime}:F>";

        ComponentContainerProperties container =
            [
                new TextDisplayProperties($"## Zenless Zone Zero Real-Time Notes (UID: {gameUid})"),
                new ComponentSectionProperties(
                        new ComponentSectionThumbnailProperties(
                            new ComponentMediaProperties("attachment://zzz_battery.png")))
                    .WithComponents([
                        new TextDisplayProperties("### Battery Charge"),
                        new TextDisplayProperties($"{data.Energy.Progress.Current}/{data.Energy.Progress.Max}"),
                        new TextDisplayProperties(data.Energy.Restore == 0
                            ? "Fully Recovered!"
                            : $"-# Recovers <t:{currTime + data.Energy.Restore}:R>")
                    ]),
                new TextDisplayProperties("### Daily Missions\n"),
                new TextDisplayProperties($"Daily Engagement: {data.Vitality.Current}/{data.Vitality.Max}\n"),
                new TextDisplayProperties($"Scratch Card/Divination: {data.CardSign.ToReadableString()}\n"),
                new TextDisplayProperties($"Video Store Management: {data.VhsSale.SaleState.ToReadableString()}"),
                new TextDisplayProperties("### Season Missions\n"),
                new TextDisplayProperties($"Bounty Commission: {bounty}\n"),
                new TextDisplayProperties($"Ridu Weekly Points: {weeklyTask}\n"),
                new TextDisplayProperties($"Omnicoins: {omnicoins}"),
                new TextDisplayProperties("### Suibian Temple Management\n"),
                new TextDisplayProperties($"Adventure: {data.TempleManage.ExpeditionState.ToReadableString()}\n"),
                new TextDisplayProperties($"Crafting Workshop: {data.TempleManage.BenchState.ToReadableString()}\n"),
                new TextDisplayProperties($"Sales Stall: {data.TempleManage.ShelveState.ToReadableString()}")
            ];

        response.AddAttachments(
            new AttachmentProperties("zzz_battery.png", stamImage)
        );

        response.WithComponents([container]);
        return response;
    }
}
