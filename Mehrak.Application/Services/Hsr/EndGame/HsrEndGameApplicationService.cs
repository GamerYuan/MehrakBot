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

            var profile = await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.HonkaiStarRail, region);

            if (profile == null)
            {
                Logger.LogWarning("No profile found for user {UserId}", context.UserId);
                return CommandResult.Failure("Invalid HoYoLAB UID or Cookies. Please authenticate again");
            }

            var challengeResponse = await m_ApiService.GetAsync(
                new(context.UserId, context.LtUid, context.LToken, profile.GameUid, region, context.Mode));
            if (!challengeResponse.IsSuccess)
            {
                Logger.LogError("Failed to fetch {GameMode} data for user {UserId}: {ErrorMessage}",
                    context.Mode.GetString(), context.UserId, challengeResponse.ErrorMessage);
                return CommandResult.Failure(challengeResponse.ErrorMessage);
            }

            var challengeData = challengeResponse.Data;
            if (!challengeData.HasData)
            {
                Logger.LogInformation("No {GameMode} clear records found for user {UserId}",
                    context.Mode.GetString(), context.UserId);
                return CommandResult.Failure($"No {context.Mode.GetString()} clear records found!");
            }

            var nonNull = challengeData.AllFloorDetail.Where(x => x is { Node1: not null, Node2: not null }).ToList();
            if (nonNull.Count == 0)
            {
                Logger.LogInformation("No Apocalyptic Shadow clear records found for user {UserId}",
                    context.UserId);
                return CommandResult.Failure($"No {context.Mode.GetString()} clear records found!");
            }

            var tasks = nonNull
                .SelectMany(x => x.Node1!.Avatars.Concat(x.Node2!.Avatars))
                .DistinctBy(x => x.Id)
                .Select(x => m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(), ImageProcessors.AvatarProcessor));
            var buffTasks = challengeData.AllFloorDetail.Where(x => !x.IsFast)
                .SelectMany(x => new List<HsrEndBuff> { x.Node1!.Buff, x.Node2!.Buff })
                .DistinctBy(x => x.Id)
                .Select(x => m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(), new ImageProcessorBuilder().Build()));

            await Task.WhenAll(tasks.Concat(buffTasks));

            var card = await m_CardService.GetCardAsync(new(context.UserId, challengeData, context.Server, profile, context.Mode));

            var tz = context.Server.GetTimeZoneInfo();
            var group = challengeData.Groups[0];
            var startTime = new DateTimeOffset(group.BeginTime.ToDateTime(), tz.BaseUtcOffset)
                .ToUnixTimeSeconds();
            var endTime = new DateTimeOffset(group.EndTime.ToDateTime(), tz.BaseUtcOffset)
                .ToUnixTimeSeconds();

            return CommandResult.Success([
                new CommandText($"<@{context.UserId}>'s {context.Mode.GetString()} Summary", CommandText.TextType.Header3),
                new CommandText($"Cycle start: <t:{startTime}:f>\nCycle end: <t:{endTime}:f>"),
                new CommandAttachment($"{context.Mode.GetString().ToLowerInvariant().Replace(' ', '_')}_card.jpg", card),
                new CommandText($"-# Information may be inaccurate due to API limitations. Please check in-game for the most accurate data.",
                    CommandText.TextType.Footer)],
                true
            );
        }
        catch (CommandException e)
        {
            Logger.LogError(e, "Error processing Apocalyptic Shadow card for user {UserId}",
                context.UserId);
            return CommandResult.Failure(e.Message);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error processing Apocalyptic Shadow card for user {UserId}",
                context.UserId);
            return CommandResult.Failure("An error occurred while generating Apocalyptic Shadow card");
        }
    }
}

internal static class HsrEndGameModeExtensions
{
    public static string GetString(this HsrEndGameMode mode)
    {
        return mode switch
        {
            HsrEndGameMode.PureFiction => "Pure Fiction",
            HsrEndGameMode.ApocalypticShadow => "Apocalyptic Shadow",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };
    }
}
