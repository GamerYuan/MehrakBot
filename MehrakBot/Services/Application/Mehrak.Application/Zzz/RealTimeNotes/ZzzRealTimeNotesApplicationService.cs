#region

using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Services;
using Mehrak.Application.Shared.Utility;
using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.Domain.User.Models;
using Mehrak.GameApi.GameRole;
using Mehrak.GameApi.Shared.Types;
using Mehrak.GameApi.Zzz.Types;
using Mehrak.Infrastructure.User;

#endregion

namespace Mehrak.Application.Zzz.RealTimeNotes;

internal class ZzzRealTimeNotesApplicationService : BaseApplicationService
{
    private readonly IApiService<ZzzRealTimeNotesData, BaseHoYoApiContext> m_ApiService;

    protected override string CommandName => "Notes";
    public ZzzRealTimeNotesApplicationService(
        IApiService<ZzzRealTimeNotesData, BaseHoYoApiContext> apiService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        UserDbContext userContext,
        ILogger<ZzzRealTimeNotesApplicationService> logger)
        : base(gameRoleApi, userContext, logger)
    {
        m_ApiService = apiService;
    }

    protected override async Task<CommandResult> ExecuteCommandAsync(IApplicationContext context, CancellationToken cancellationToken = default)
    {
        var server = Enum.Parse<Server>(context.GetParameter("server")!);
        var region = server.ToRegion();

        var (profile, notesResult) = await FetchProfileAndPrimaryAsync(
            context.UserId, context.LtUid, context.LToken, Game.ZenlessZoneZero, region,
            uid => m_ApiService.GetAsync(
                new BaseHoYoApiContext(context.UserId, context.LtUid, context.LToken, uid, region), cancellationToken),
            cancellationToken);

        var gameUid = profile.GameUid;

        if (!notesResult.IsSuccess)
        {
            if (notesResult.StatusCode == StatusCode.Cancelled)
                throw new OperationCanceledException(notesResult.ErrorMessage ?? "Cancelled");
            if (notesResult.StatusCode == StatusCode.Timeout)
                return CommandResult.Failure(CommandFailureReason.Timeout, ResponseMessage.TimeoutError);
            Logger.LogError(LogMessage.ApiError, "Notes", context.UserId, gameUid, notesResult);
            return CommandResult.Failure(CommandFailureReason.ApiError,
                string.Format(ResponseMessage.ApiError, "Real-Time Notes"));
        }

        var notesData = notesResult.Data;

        return await BuildRealTimeNotes(notesData, gameUid);
    }

    private async Task<CommandResult> BuildRealTimeNotes(ZzzRealTimeNotesData data, string uid)
    {
        var currTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var omnicoins = data.TempleManage.CurrencyNextRefreshTs == 0
            ? "-"
            : $"{data.TempleManage.CurrentCurrency}/{data.TempleManage.WeeklyCurrencyMax}" +
              $"-# Refreshes in <t:{data.TempleManage.CurrencyNextRefreshTs}:F>";
        var bounty = data.BountyCommission.Total == 0
            ? "-"
            : $"{data.BountyCommission.Num}/{data.BountyCommission.Total}\n" +
              $"-# Refreshes in <t:{currTime + data.BountyCommission.RefreshTime}:F>";
        var weeklyTask = data.WeeklyTask is null
            ? "-"
            : $"{data.WeeklyTask!.CurPoint}/{data.WeeklyTask!.MaxPoint}\n" +
              $"-# Refreshes in <t:{currTime + data.WeeklyTask.RefreshTime}:F>";

        List<ICommandResultComponent> components =
        [
            new CommandText($"Zenless Zone Zero Real-Time Notes (UID: {uid})", CommandText.TextType.Header2),
            new CommandSection([
                    new CommandText("Battery Charge", CommandText.TextType.Header3),
                    new CommandText($"{data.Energy.Progress.Current}/{data.Energy.Progress.Max}"),
                    new CommandText(data.Energy.Restore == 0
                        ? "Fully Recovered!"
                        : $"Recovers <t:{currTime + data.Energy.Restore}:R>", CommandText.TextType.Footer)
                ],
                new CommandAttachment("zzz_battery.png", AttachmentSourceType.ImageStorage, "zzz/battery.png")
            ),
            new CommandText("Daily Missions", CommandText.TextType.Header3),
            new CommandText($"Daily Engagement: {data.Vitality.Current}/{data.Vitality.Max}"),
            new CommandText($"Scratch Card/Divination: {data.CardSign.ToReadableString()}"),
            new CommandText($"Video Store Management: {data.VhsSale.SaleState.ToReadableString()}"),
            new CommandText("Season Missions", CommandText.TextType.Header3),
            new CommandText($"Bounty Commission: {bounty}"),
            new CommandText($"Ridu Weekly Points: {weeklyTask}"),
            new CommandText($"Omnicoins: {omnicoins}"),
            new CommandText("Suibian Temple Management", CommandText.TextType.Header3),
            new CommandText($"Adventure: {data.TempleManage.ExpeditionState.ToReadableString()}"),
            new CommandText($"Crafting Workshop: {data.TempleManage.BenchState.ToReadableString()}"),
            new CommandText($"Sales Stall: {data.TempleManage.ShelveState.ToReadableString()}")
        ];

        return CommandResult.Success(components, true, true);
    }
}
