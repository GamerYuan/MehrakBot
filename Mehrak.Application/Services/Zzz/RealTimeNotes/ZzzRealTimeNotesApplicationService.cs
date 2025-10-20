using Mehrak.Application.Services.Common;
using Mehrak.Domain.Common;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Zzz.Types;
using Microsoft.Extensions.Logging;

namespace Mehrak.Application.Services.Zzz.RealTimeNotes;

internal class ZzzRealTimeNotesApplicationService : BaseApplicationService<ZzzRealTimeNotesApplicationContext>
{
    private readonly IImageRepository m_ImageRepository;
    private readonly IApiService<ZzzRealTimeNotesData, BaseHoYoApiContext> m_ApiService;

    public ZzzRealTimeNotesApplicationService(
        IImageRepository imageRepository,
        IApiService<ZzzRealTimeNotesData, BaseHoYoApiContext> apiService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        ILogger<ZzzRealTimeNotesApplicationService> logger)
        : base(gameRoleApi, logger)
    {
        m_ImageRepository = imageRepository;
        m_ApiService = apiService;
    }

    public override async Task<CommandResult> ExecuteAsync(ZzzRealTimeNotesApplicationContext context)
    {
        try
        {
            string region = context.Server.ToRegion();

            var profile = await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.ZenlessZoneZero, region);

            if (profile == null)
            {
                Logger.LogWarning("No profile found for user {UserId}", context.UserId);
                return CommandResult.Failure("Invalid HoYoLAB UID or Cookies. Please authenticate again");
            }

            var gameUid = profile.GameUid;

            var notesResult = await m_ApiService.GetAsync(
                new(context.UserId, context.LtUid, context.LToken, gameUid, region));

            if (!notesResult.IsSuccess)
            {
                Logger.LogWarning("Failed to fetch Real Time Notes information for gameUid: {GameUid}, region: {Server}, error: {Error}",
                    profile.GameUid, context.Server, notesResult.ErrorMessage);
                return CommandResult.Failure(notesResult.ErrorMessage);
            }

            ZzzRealTimeNotesData notesData = notesResult.Data;
            if (notesData == null)
            {
                Logger.LogWarning("No data found in real-time notes response");
                return CommandResult.Failure("No data found in real-time notes response");
            }

            return await BuildRealTimeNotes(notesData, context.Server, gameUid);
        }
        catch (CommandException e)
        {
            Logger.LogError(e, "Error fetching real-time notes for user {UserId} in region {Region}",
                context.UserId, context.Server);
            return CommandResult.Failure(e.Message);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error fetching real-time notes for user {UserId} in region {Region}",
                context.UserId, context.Server);
            return CommandResult.Failure("An error occurred while retrieving Real Time Notes data");
        }
    }

    private async Task<CommandResult> BuildRealTimeNotes(ZzzRealTimeNotesData data,
        Server server, string uid)
    {
        Stream stamImage = await m_ImageRepository.DownloadFileToStreamAsync("zzz_battery");

        long currTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

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

        List<CommandSection> sections =
        [
            new("Battery Charge",
                $"{data.Energy.Progress.Current}/{data.Energy.Progress.Max}",
                data.Energy.Restore == 0
                    ? "Fully Recovered!"
                    : $"-# Recovers <t:{currTime + data.Energy.Restore}:R>",
                new("zzz_battery.png", stamImage)
            )
        ];

        List<CommandText> texts =
        [
            new("Daily Missions", CommandText.TextType.Header3),
            new ($"Daily Engagement: {data.Vitality.Current}/{data.Vitality.Max}"),
            new ($"Scratch Card/Divination: {data.CardSign.ToReadableString()}"),
            new ($"Video Store Management: {data.VhsSale.SaleState.ToReadableString()}"),
            new("Season Missions", CommandText.TextType.Header3),
            new ($"Bounty Commission: {bounty}"),
            new ($"Ridu Weekly Points: {weeklyTask}"),
            new ($"Omnicoins: {omnicoins}"),
            new("Suibian Temple Management", CommandText.TextType.Header3),
            new ($"Adventure: {data.TempleManage.ExpeditionState.ToReadableString()}"),
            new ($"Crafting Workshop: {data.TempleManage.BenchState.ToReadableString()}"),
            new ($"Sales Stall: {data.TempleManage.ShelveState.ToReadableString()}")
        ];

        return CommandResult.Success($"Zenless Zone Zero Real-Time Notes (UID: {uid})", sections: sections, texts: texts);
    }
}
