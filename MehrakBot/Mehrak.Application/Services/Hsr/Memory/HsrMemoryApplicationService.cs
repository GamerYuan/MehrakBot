#region

using System.Text.Json;
using Mehrak.Application.Services.Common;
using Mehrak.Application.Services.Genshin.Types;
using Mehrak.Application.Utility;
using Mehrak.Domain.Common;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.Domain.Utility;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Hsr.Types;
using Microsoft.Extensions.Logging;

#endregion

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
        IUserRepository userRepository,
        ILogger<HsrMemoryApplicationService> logger)
        : base(gameRoleApi, userRepository, logger)
    {
        m_CardService = cardService;
        m_ImageUpdaterService = imageUpdaterService;
        m_ApiService = apiService;
    }

    public override async Task<CommandResult> ExecuteAsync(HsrMemoryApplicationContext context)
    {
        try
        {
            var server = Enum.Parse<Server>(context.GetParameter<string>("server")!);
            var region = server.ToRegion();

            var profile = await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken,
                Game.HonkaiStarRail, region);

            if (profile == null)
            {
                Logger.LogWarning(LogMessage.InvalidLogin, context.UserId);
                return CommandResult.Failure(CommandFailureReason.AuthError, ResponseMessage.AuthError);
            }

            await UpdateGameUidAsync(context.UserId, context.LtUid, Game.HonkaiStarRail, profile.GameUid, server);

            var gameUid = profile.GameUid;
            var memoryResult =
                await m_ApiService.GetAsync(new BaseHoYoApiContext(context.UserId, context.LtUid, context.LToken,
                    gameUid, region));
            if (!memoryResult.IsSuccess)
            {
                Logger.LogError(LogMessage.ApiError, "Memory of Chaos", context.UserId, gameUid, memoryResult);
                return CommandResult.Failure(CommandFailureReason.ApiError,
                    string.Format(ResponseMessage.ApiError, "Memory of Chaos data"));
            }

            var memoryData = memoryResult.Data;

            if (!memoryData.HasData || memoryData.BattleNum == 0)
            {
                Logger.LogInformation(LogMessage.NoClearRecords, "Memory of Chaos", context.UserId, gameUid);
                return CommandResult.Success(
                    [new CommandText(string.Format(ResponseMessage.NoClearRecords, "Memory of Chaos"))],
                    isEphemeral: true);
            }

            var tasks = memoryData.AllFloorDetail!.SelectMany(x => x.Node1.Avatars.Concat(x.Node2.Avatars))
                .DistinctBy(x => x.Id)
                .Select(x =>
                    m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(), ImageProcessors.AvatarProcessor));
            var completed = await Task.WhenAll(tasks);

            if (completed.Any(x => !x))
            {
                Logger.LogError(LogMessage.ImageUpdateError, "Memory of Chaos", context.UserId,
                    JsonSerializer.Serialize(memoryData));
                return CommandResult.Failure(CommandFailureReason.ApiError, ResponseMessage.ImageUpdateError);
            }

            var card = await m_CardService.GetCardAsync(
                new BaseCardGenerationContext<HsrMemoryInformation>(context.UserId, memoryData, server,
                    profile));

            var tz = server.GetTimeZoneInfo();
            var startTime = new DateTimeOffset(memoryData.StartTime.ToDateTime(), tz.BaseUtcOffset)
                .ToUnixTimeSeconds();
            var endTime = new DateTimeOffset(memoryData.EndTime.ToDateTime(), tz.BaseUtcOffset)
                .ToUnixTimeSeconds();

            return CommandResult.Success(
                [
                    new CommandText($"<@{context.UserId}>'s Memory of Chaos Summary", CommandText.TextType.Header3),
                    new CommandText($"Cycle start: <t:{startTime}:f>\nCycle end: <t:{endTime}:f>"),
                    new CommandAttachment("moc_card.jpg", card),
                    new CommandText(ResponseMessage.ApiLimitationFooter, CommandText.TextType.Footer)
                ],
                true);
        }
        catch (CommandException e)
        {
            Logger.LogError(e, LogMessage.UnknownError, "Memory of Chaos", context.UserId, e.Message);
            return CommandResult.Failure(CommandFailureReason.BotError,
                string.Format(ResponseMessage.CardGenError, "Memory of Chaos"));
        }
        catch (Exception e)
        {
            Logger.LogError(e, LogMessage.UnknownError, "Memory of Chaos", context.UserId, e.Message);
            return CommandResult.Failure(CommandFailureReason.Unknown, ResponseMessage.UnknownError);
        }
    }
}
