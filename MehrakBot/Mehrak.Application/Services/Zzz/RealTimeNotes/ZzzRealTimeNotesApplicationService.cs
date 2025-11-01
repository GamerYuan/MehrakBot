﻿#region

using Mehrak.Application.Services.Common;
using Mehrak.Application.Utility;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Zzz.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.Application.Services.Zzz.RealTimeNotes;

internal class ZzzRealTimeNotesApplicationService : BaseApplicationService<ZzzRealTimeNotesApplicationContext>
{
    private readonly IImageRepository m_ImageRepository;
    private readonly IApiService<ZzzRealTimeNotesData, BaseHoYoApiContext> m_ApiService;

    public ZzzRealTimeNotesApplicationService(
        IImageRepository imageRepository,
        IApiService<ZzzRealTimeNotesData, BaseHoYoApiContext> apiService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        IUserRepository userRepository,
        ILogger<ZzzRealTimeNotesApplicationService> logger)
        : base(gameRoleApi, userRepository, logger)
    {
        m_ImageRepository = imageRepository;
        m_ApiService = apiService;
    }

    public override async Task<CommandResult> ExecuteAsync(ZzzRealTimeNotesApplicationContext context)
    {
        try
        {
            string region = context.Server.ToRegion();

            var profile = await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.ZenlessZoneZero,
                region);

            if (profile == null)
            {
                Logger.LogWarning(LogMessage.InvalidLogin, context.UserId);
                return CommandResult.Failure(CommandFailureReason.AuthError, ResponseMessage.AuthError);
            }

            await UpdateGameUidAsync(context.UserId, context.LtUid, Game.ZenlessZoneZero, profile.GameUid, context.Server);

            var gameUid = profile.GameUid;

            var notesResult = await m_ApiService.GetAsync(
                new BaseHoYoApiContext(context.UserId, context.LtUid, context.LToken, gameUid, region));

            if (!notesResult.IsSuccess)
            {
                Logger.LogError(LogMessage.ApiError, "Notes", context.UserId, gameUid, notesResult);
                return CommandResult.Failure(CommandFailureReason.ApiError,
                    string.Format(ResponseMessage.ApiError, "Real-Time Notes"));
            }

            ZzzRealTimeNotesData notesData = notesResult.Data;

            return await BuildRealTimeNotes(notesData, gameUid);
        }
        catch (Exception e)
        {
            Logger.LogError(e, LogMessage.UnknownError, "Notes", context.UserId, e.Message);
            return CommandResult.Failure(CommandFailureReason.Unknown, ResponseMessage.UnknownError);
        }
    }

    private async Task<CommandResult> BuildRealTimeNotes(ZzzRealTimeNotesData data, string uid)
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
                new CommandAttachment("zzz_battery.png", stamImage)
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
