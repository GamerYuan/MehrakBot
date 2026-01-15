using System.Text.Json;
using Mehrak.Application.Builders;
using Mehrak.Application.Services.Common;
using Mehrak.Application.Services.Common.Types;
using Mehrak.Application.Utility;
using Mehrak.Domain.Common;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.Domain.Utility;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Hsr.Types;
using Mehrak.Infrastructure.Context;
using Microsoft.Extensions.Logging;

namespace Mehrak.Application.Services.Hsr.Anomaly;

internal class HsrAnomalyApplicationService : BaseAttachmentApplicationService<HsrAnomalyApplicationContext>
{
    private readonly ICardService<HsrAnomalyInformation> m_CardService;
    private readonly IImageUpdaterService m_ImageUpdaterService;
    private readonly IApiService<HsrAnomalyInformation, BaseHoYoApiContext> m_ApiService;

    public HsrAnomalyApplicationService(
        ICardService<HsrAnomalyInformation> cardService,
        IImageUpdaterService imageUpdaterService,
        IApiService<HsrAnomalyInformation, BaseHoYoApiContext> apiService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        UserDbContext userContext,
        IAttachmentStorageService attachmentStorageService,
        ILogger<HsrAnomalyApplicationService> logger)
        : base(gameRoleApi, userContext, attachmentStorageService, logger)
    {
        m_CardService = cardService;
        m_ImageUpdaterService = imageUpdaterService;
        m_ApiService = apiService;
    }

    public override async Task<CommandResult> ExecuteAsync(HsrAnomalyApplicationContext context)
    {
        try
        {
            var server = Enum.Parse<Server>(context.GetParameter("server")!);
            var region = server.ToRegion();

            var profile = await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken,
                Game.HonkaiStarRail, region);

            if (profile == null)
            {
                Logger.LogWarning(LogMessage.InvalidLogin, context.UserId);
                return CommandResult.Failure(CommandFailureReason.AuthError, ResponseMessage.AuthError);
            }

            await UpdateGameUidAsync(context.UserId, context.LtUid, Game.HonkaiStarRail, profile.GameUid, server.ToString());

            var gameUid = profile.GameUid;
            var anomalyResult =
                await m_ApiService.GetAsync(new BaseHoYoApiContext(context.UserId, context.LtUid, context.LToken,
                    gameUid, region));
            if (!anomalyResult.IsSuccess)
            {
                Logger.LogError(LogMessage.ApiError, "Anomaly Arbitration", context.UserId, gameUid, anomalyResult);
                return CommandResult.Failure(CommandFailureReason.ApiError,
                    string.Format(ResponseMessage.ApiError, "Anomaly Arbitration data"));
            }

            var anomalyData = anomalyResult.Data;

            if (anomalyData.ChallengeRecords.Count == 0)
            {
                Logger.LogInformation(LogMessage.NoClearRecords, "Anomaly Arbitration", context.UserId, gameUid);
                return CommandResult.Success(
                    [new CommandText(string.Format(ResponseMessage.NoClearRecords, "Anomaly Arbitration"))],
                    isEphemeral: true);
            }

            var bestRecord = anomalyData.BestRecord.RankIconType != RankIconType.ChallengePeakRankIconTypeNone
                ? anomalyData.ChallengeRecords.FirstOrDefault(
                    x => x.HasChallengeRecord && x.BossStars == anomalyData.BestRecord.BossStars
                        && x.MobStars == anomalyData.BestRecord.MobStars)
                : anomalyData.ChallengeRecords.FirstOrDefault(x => x.HasChallengeRecord && x.MobStars == anomalyData.BestRecord.MobStars);

            if (bestRecord == null)
            {
                Logger.LogInformation(LogMessage.NoClearRecords, "Anomaly Arbitration", context.UserId, gameUid);
                return CommandResult.Success(
                    [new CommandText(string.Format(ResponseMessage.NoClearRecords, "Anomaly Arbitration"))],
                    isEphemeral: true);
            }

            var tz = server.GetTimeZoneInfo();
            var startTime = new DateTimeOffset(bestRecord.Group.BeginTime.ToDateTime(), tz.BaseUtcOffset)
                .ToUnixTimeSeconds();
            var endTime = new DateTimeOffset(bestRecord.Group.EndTime.ToDateTime(), tz.BaseUtcOffset)
                .ToUnixTimeSeconds();

            var fileName = GetFileName(JsonSerializer.Serialize(anomalyData), "jpg", gameUid);
            if (await AttachmentExistsAsync(fileName))
            {
                return CommandResult.Success(
                    [
                        new CommandText($"<@{context.UserId}>'s Anomaly Arbitration Summary", CommandText.TextType.Header3),
                        new CommandText($"Cycle start: <t:{startTime}:f>\nCycle end: <t:{endTime}:f>"),
                        new CommandAttachment(fileName),
                        new CommandText(ResponseMessage.ApiLimitationFooter, CommandText.TextType.Footer)
                    ],
                    true);
            }

            List<Task<bool>> tasks = [];

            tasks.AddRange(bestRecord.MobRecords.SelectMany(x => x.Avatars)
                .Concat(bestRecord.BossRecord?.Avatars ?? [])
                .DistinctBy(x => x.Id)
                .Select(x => m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(), ImageProcessors.AvatarProcessor)));

            tasks.Add(m_ImageUpdaterService.UpdateImageAsync(bestRecord.BossInfo.ToImageData(),
                new ImageProcessorBuilder().Resize(350, 0).AddOperation(x => x.ApplyGradientFade()).Build()));

            tasks.Add(m_ImageUpdaterService.UpdateImageAsync(anomalyData.ToMedalIconData(),
                new ImageProcessorBuilder().Resize(120, 0).Build()));

            if (bestRecord.BossRecord != null)
            {
                tasks.Add(m_ImageUpdaterService.UpdateImageAsync(bestRecord.BossRecord.Buff.ToImageData(),
                    new ImageProcessorBuilder().Build()));
            }

            var completed = await Task.WhenAll(tasks);
            if (completed.Any(x => !x))
            {
                Logger.LogError(LogMessage.ImageUpdateError, "Anomaly Arbitration", context.UserId,
                    JsonSerializer.Serialize(anomalyData));
                return CommandResult.Failure(CommandFailureReason.BotError, ResponseMessage.ImageUpdateError);
            }

            var cardContext = new BaseCardGenerationContext<HsrAnomalyInformation>(context.UserId, anomalyData, profile);
            cardContext.SetParameter("server", server);

            await using var card = await m_CardService.GetCardAsync(cardContext);

            if (!await StoreAttachmentAsync(context.UserId, fileName, card))
            {
                Logger.LogError(LogMessage.AttachmentStoreError, fileName, context.UserId);
                return CommandResult.Failure(CommandFailureReason.BotError,
                    ResponseMessage.AttachmentStoreError);
            }

            return CommandResult.Success(
                [
                    new CommandText($"<@{context.UserId}>'s Anomaly Arbitration Summary", CommandText.TextType.Header3),
                    new CommandText($"Cycle start: <t:{startTime}:f>\nCycle end: <t:{endTime}:f>"),
                    new CommandAttachment(fileName),
                    new CommandText(ResponseMessage.ApiLimitationFooter, CommandText.TextType.Footer)
                ],
                true);
        }
        catch (CommandException e)
        {
            Logger.LogError(e, LogMessage.UnknownError, "Anomaly Arbitration", context.UserId, e.Message);
            return CommandResult.Failure(CommandFailureReason.BotError,
                string.Format(ResponseMessage.CardGenError, "Anomaly Arbitration"));
        }
        catch (Exception e)
        {
            Logger.LogError(e, LogMessage.UnknownError, "Anomaly Arbitration", context.UserId, e.Message);
            return CommandResult.Failure(CommandFailureReason.Unknown, ResponseMessage.UnknownError);
        }
    }
}
