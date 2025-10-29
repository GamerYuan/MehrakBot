#region

using System.Text.Json;
using Mehrak.Application.Builders;
using Mehrak.Application.Services.Common;
using Mehrak.Application.Services.Hsr.Types;
using Mehrak.Application.Utility;
using Mehrak.Domain.Common;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.Domain.Utility;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Hsr.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.Application.Services.Hsr.EndGame;

public class HsrEndGameApplicationService : BaseApplicationService<HsrEndGameApplicationContext>
{
    private readonly ICardService<HsrEndGameGenerationContext, HsrEndInformation> m_CardService;
    private readonly IImageUpdaterService m_ImageUpdaterService;
    private readonly IApiService<HsrEndInformation, HsrEndGameApiContext> m_ApiService;

    public HsrEndGameApplicationService(
        ICardService<HsrEndGameGenerationContext, HsrEndInformation> cardService,
        IImageUpdaterService imageUpdaterService,
        IApiService<HsrEndInformation, HsrEndGameApiContext> apiService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        ILogger<HsrEndGameApplicationService> logger) : base(gameRoleApi, logger)
    {
        m_CardService = cardService;
        m_ImageUpdaterService = imageUpdaterService;
        m_ApiService = apiService;
    }

    public override async Task<CommandResult> ExecuteAsync(HsrEndGameApplicationContext context)
    {
        try
        {
            var region = context.Server.ToRegion();

            var profile = await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.HonkaiStarRail,
                region);

            if (profile == null)
            {
                Logger.LogWarning(LogMessage.InvalidLogin, context.UserId);
                return CommandResult.Failure(CommandFailureReason.AuthError, ResponseMessage.AuthError);
            }

            var challengeResponse = await m_ApiService.GetAsync(
                new HsrEndGameApiContext(context.UserId, context.LtUid, context.LToken, profile.GameUid, region,
                    context.Mode));
            if (!challengeResponse.IsSuccess)
            {
                Logger.LogError(LogMessage.ApiError, context.Mode.GetString(), context.UserId, profile.GameUid,
                    challengeResponse);
                return CommandResult.Failure(CommandFailureReason.ApiError,
                    string.Format(ResponseMessage.ApiError, $"{context.Mode.GetString()} data"));
            }

            var challengeData = challengeResponse.Data;
            if (!challengeData.HasData)
            {
                Logger.LogInformation(LogMessage.NoClearRecords, context.Mode.GetString(), context.UserId,
                    profile.GameUid);
                return CommandResult.Success(
                    [new CommandText(string.Format(ResponseMessage.NoClearRecords, context.Mode.GetString()))],
                    isEphemeral: true);
            }

            var nonNull = challengeData.AllFloorDetail.Where(x => x is { Node1: not null, Node2: not null }).ToList();
            if (nonNull.Count == 0)
            {
                Logger.LogInformation(LogMessage.NoClearRecords, context.Mode.GetString(), context.UserId,
                    profile.GameUid);
                return CommandResult.Success(
                    [new CommandText(string.Format(ResponseMessage.NoClearRecords, context.Mode.GetString()))],
                    isEphemeral: true);
            }

            var tasks = nonNull
                .SelectMany(x => x.Node1!.Avatars.Concat(x.Node2!.Avatars))
                .DistinctBy(x => x.Id)
                .Select(x => m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(), ImageProcessors.AvatarProcessor));
            var buffTasks = challengeData.AllFloorDetail.Where(x => !x.IsFast)
                .SelectMany(x => new List<HsrEndBuff> { x.Node1!.Buff, x.Node2!.Buff })
                .DistinctBy(x => x.Id)
                .Select(x =>
                    m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(), new ImageProcessorBuilder().Build()));

            var completed = await Task.WhenAll(tasks.Concat(buffTasks));

            if (completed.Any(x => !x))
            {
                Logger.LogError(LogMessage.ImageUpdateError, context.Mode.GetString(), context.UserId,
                    JsonSerializer.Serialize(challengeData));
                return CommandResult.Failure(CommandFailureReason.ApiError, ResponseMessage.ImageUpdateError);
            }

            var card = await m_CardService.GetCardAsync(new HsrEndGameGenerationContext(context.UserId, challengeData,
                context.Server, profile, context.Mode));

            var tz = context.Server.GetTimeZoneInfo();
            var group = challengeData.Groups[0];
            var startTime = new DateTimeOffset(group.BeginTime.ToDateTime(), tz.BaseUtcOffset)
                .ToUnixTimeSeconds();
            var endTime = new DateTimeOffset(group.EndTime.ToDateTime(), tz.BaseUtcOffset)
                .ToUnixTimeSeconds();

            return CommandResult.Success([
                    new CommandText($"<@{context.UserId}>'s {context.Mode.GetString()} Summary",
                        CommandText.TextType.Header3),
                    new CommandText($"Cycle start: <t:{startTime}:f>\nCycle end: <t:{endTime}:f>"),
                    new CommandAttachment($"{context.Mode.GetString().ToLowerInvariant().Replace(' ', '_')}_card.jpg",
                        card),
                    new CommandText(ResponseMessage.ApiLimitationFooter, CommandText.TextType.Footer)
                ],
                true
            );
        }
        catch (CommandException e)
        {
            Logger.LogError(e, LogMessage.UnknownError, context.Mode.GetString(), context.UserId, e.Message);
            return CommandResult.Failure(CommandFailureReason.BotError,
                string.Format(ResponseMessage.CardGenError, context.Mode.GetString()));
        }
        catch (Exception e)
        {
            Logger.LogError(e, LogMessage.UnknownError, context.Mode.GetString(), context.UserId, e.Message);
            return CommandResult.Failure(CommandFailureReason.Unknown, ResponseMessage.UnknownError);
        }
    }
}
