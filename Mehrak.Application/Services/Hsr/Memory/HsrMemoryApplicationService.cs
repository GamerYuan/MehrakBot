using Mehrak.Application.Services.Common;
using Mehrak.Application.Services.Genshin.Types;
using Mehrak.Application.Utility;
using Mehrak.Domain.Common;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.Domain.Utility;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Hsr.Types;
using Microsoft.Extensions.Logging;

namespace Mehrak.Application.Services.Hsr.Memory;

internal class HsrMemoryApplicationService : BaseApplicationService<HsrMemoryApplicationContext>
{
    private readonly ICardService<HsrMemoryInformation> m_CardService;
    private readonly IImageUpdaterService m_ImageUpdaterService;
    private readonly IApiService<HsrMemoryInformation, BaseHoYoApiContext> m_ApiService;

    public HsrMemoryApplicationService(
        ICardService<HsrMemoryInformation> cardService,
        IImageUpdaterService imageUpdaterService,
        IApiService<HsrMemoryInformation, BaseHoYoApiContext> apiService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        ILogger<HsrMemoryApplicationService> logger)
        : base(gameRoleApi, logger)
    {
        m_CardService = cardService;
        m_ImageUpdaterService = imageUpdaterService;
        m_ApiService = apiService;
    }

    public override async Task<CommandResult> ExecuteAsync(HsrMemoryApplicationContext context)
    {
        try
        {
            var region = context.Server.ToRegion();

            var profile = await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken,
                Game.HonkaiStarRail, region);

            if (profile == null)
            {
                Logger.LogWarning("No profile found for user {UserId}", context.UserId);
                return CommandResult.Failure("Invalid HoYoLAB UID or Cookies. Please authenticate again");
            }

            var gameUid = profile.GameUid;
            var memoryResult = await m_ApiService.GetAsync(new(context.UserId, context.LtUid, context.LToken, gameUid, region));
            if (!memoryResult.IsSuccess)
            {
                Logger.LogWarning(
                    "Failed to fetch Memory of Chaos information for gameUid: {GameUid}, region: {Region}, error: {Error}",
                    gameUid, region, memoryResult.ErrorMessage);
                return CommandResult.Failure(memoryResult.ErrorMessage);
            }

            var memoryData = memoryResult.Data;

            if (!memoryData.HasData || memoryData.BattleNum == 0)
            {
                Logger.LogInformation(
                    "No Memory of Chaos data found for user {UserId} in region {Region}", context.UserId, region);
                return CommandResult.Failure("No Memory of Chaos clear record found");
            }

            var tasks = memoryData.AllFloorDetail!.SelectMany(x => x.Node1.Avatars.Concat(x.Node2.Avatars))
                .DistinctBy(x => x.Id)
                .Select(x =>
                    m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(), ImageProcessors.AvatarProcessor));
            await Task.WhenAll(tasks);

            var card = await m_CardService.GetCardAsync(
                new BaseCardGenerationContext<HsrMemoryInformation>(context.UserId, memoryData, context.Server, profile));

            var tz = context.Server.GetTimeZoneInfo();
            var startTime = new DateTimeOffset(memoryData.StartTime.ToDateTime(), tz.BaseUtcOffset)
                .ToUnixTimeSeconds();
            var endTime = new DateTimeOffset(memoryData.EndTime.ToDateTime(), tz.BaseUtcOffset)
                .ToUnixTimeSeconds();

            return CommandResult.Success(
                [new CommandText($"<@{context.UserId}>'s Memory of Chaos Summary", CommandText.TextType.Header3),
                new CommandText($"Cycle start: <t:{startTime}:f>\nCycle end: <t:{endTime}:f>"),
                new CommandAttachment("moc_card.jpg", card),
                new CommandText($"-# Information may be inaccurate due to API limitations. Please check in-game for the most accurate data.",
                    CommandText.TextType.Footer)]
                );
        }
        catch (CommandException e)
        {
            Logger.LogError(e, "Error processing Memory card for user {UserId}",
                context.UserId);
            return CommandResult.Failure(e.Message);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error processing Memory card for user {UserId}",
                context.UserId);
            return CommandResult.Failure("An error occurred while generating Memory of Chaos card");
        }
    }
}
