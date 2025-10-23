﻿using Mehrak.Application.Services.Common;
using Mehrak.Application.Utility;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.Domain.Utility;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Genshin.Types;
using Microsoft.Extensions.Logging;

namespace Mehrak.Application.Services.Genshin.RealTimeNotes;

internal class GenshinRealTimeNotesApplicationService : BaseApplicationService<GenshinRealTimeNotesApplicationContext>
{
    private readonly IImageRepository m_ImageRepository;
    private readonly IApiService<GenshinRealTimeNotesData, BaseHoYoApiContext> m_ApiService;

    public GenshinRealTimeNotesApplicationService(
        IImageRepository imageRepository,
        IApiService<GenshinRealTimeNotesData, BaseHoYoApiContext> apiService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        ILogger<GenshinRealTimeNotesApplicationService> logger)
        : base(gameRoleApi, logger)
    {
        m_ImageRepository = imageRepository;
        m_ApiService = apiService;
    }

    public override async Task<CommandResult> ExecuteAsync(GenshinRealTimeNotesApplicationContext context)
    {
        try
        {
            string region = context.Server.ToRegion();

            var profile = await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.Genshin, region);

            if (profile == null)
            {
                Logger.LogWarning(LogMessage.InvalidLogin, context.UserId);
                return CommandResult.Failure(CommandFailureReason.AuthError, ResponseMessage.AuthError);
            }

            var gameUid = profile.GameUid;

            var notesResult = await m_ApiService.GetAsync(
                new(context.UserId, context.LtUid, context.LToken, gameUid, region));

            if (!notesResult.IsSuccess)
            {
                Logger.LogError(LogMessage.ApiError, "Notes", context.UserId, gameUid, notesResult.ErrorMessage);
                return CommandResult.Failure(CommandFailureReason.ApiError, string.Format(ResponseMessage.ApiError, "Real-Time Notes data"));
            }

            return await BuildRealTimeNotes(notesResult.Data, context.Server, gameUid);
        }
        catch (Exception e)
        {
            Logger.LogError(e, LogMessage.UnknownError, "Notes", context.UserId, e.Message);
            return CommandResult.Failure(CommandFailureReason.Unknown, ResponseMessage.UnknownError);
        }
    }

    private async Task<CommandResult> BuildRealTimeNotes(GenshinRealTimeNotesData data,
        Server server, string uid)
    {
        Task<Stream> resinImage = m_ImageRepository.DownloadFileToStreamAsync("genshin_resin");
        Task<Stream> expeditionImage = m_ImageRepository.DownloadFileToStreamAsync("genshin_expedition");
        Task<Stream> teapotImage = m_ImageRepository.DownloadFileToStreamAsync("genshin_teapot");
        Task<Stream> weeklyImage = m_ImageRepository.DownloadFileToStreamAsync("genshin_weekly");
        Task<Stream> transformerImage = m_ImageRepository.DownloadFileToStreamAsync("genshin_transformer");

        long currTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long weeklyReset = server.GetNextWeeklyResetUnix();

        List<ICommandResultComponent> components = [
            new CommandText($"Real Time Notes for UID: {uid}", CommandText.TextType.Header2),
            new CommandSection([
                new("Original Resin", CommandText.TextType.Header3),
                new($"{data.CurrentResin}/{data.MaxResin}"),
                new(data.CurrentResin == data.MaxResin
                        ? "Already Full!"
                        : $"Recovers <t:{currTime + long.Parse(data.ResinRecoveryTime!)}:R>", CommandText.TextType.Footer)],
                new("genshin_resin.png", await resinImage)
                ),
            new CommandSection([
                new("Expeditions", CommandText.TextType.Header3),
                new(data.CurrentExpeditionNum > 0
                        ? $"{data.CurrentExpeditionNum}/{data.MaxExpeditionNum}"
                        : "None Dispatched!"),
                new(data.CurrentExpeditionNum > 0
                        ? data.Expeditions!.Max(x => long.Parse(x.RemainedTime!)) > 0
                            ? $"Completes <t:{currTime + data.Expeditions!.Max(x => long.Parse(x.RemainedTime!))}:R>"
                            : "All Expeditions Completed"
                        : "To be dispatched", CommandText.TextType.Footer)],
                new("genshin_expedition.png", await expeditionImage)
                ),
            new CommandSection([
                    new("Serenitea Pot", CommandText.TextType.Header3),
                    new(data.CurrentHomeCoin == data.MaxHomeCoin
                        ? "Already Full!"
                        : $"{data.CurrentHomeCoin}/{data.MaxHomeCoin}"),
                    new(data.CurrentHomeCoin == data.MaxHomeCoin
                        ? "To be collected"
                        : $"Recovers <t:{currTime + long.Parse(data.HomeCoinRecoveryTime!)}:R>", CommandText.TextType.Footer)],
                    new("genshin_teapot.png", await teapotImage)
                ),
            new CommandSection([
                    new("Weekly Bosses", CommandText.TextType.Header3),
                    new($"Remaining Resin Discount: {data.RemainResinDiscountNum}/{data.ResinDiscountNumLimit}"),
                    new($"Resets <t:{weeklyReset}:R>", CommandText.TextType.Footer)],
                    new("genshin_weekly.png", await weeklyImage)
                )

        ];

        if (data.Transformer?.Obtained == true)
        {
            components.Add(
                new CommandSection([
                    new("Parametric Transformer", CommandText.TextType.Header3),
                    new(data.Transformer.RecoveryTime!.Reached
                        ? "Not Claimed!"
                        : "Claimed!"),
                    new($"Resets <t:{weeklyReset}:R>", CommandText.TextType.Footer)],
                    new("genshin_transformer.png", await transformerImage)
                ));
        }

        return CommandResult.Success(components, true, true);
    }
}
