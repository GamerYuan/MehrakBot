#region

using System.Text.Json;
using Mehrak.Application.Services.Abstractions;
using Mehrak.Application.Services.Common;
using Mehrak.Application.Services.Common.Types;
using Mehrak.Application.Utility;
using Mehrak.Domain.Card;
using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Common;
using Mehrak.Domain.Image;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.Domain.Shared.Utility;
using Mehrak.Domain.User.Models;
using Mehrak.GameApi.GameRole;
using Mehrak.GameApi.Hsr.Types;
using Mehrak.GameApi.Shared.Types;
using Mehrak.Infrastructure.Context;

#endregion

namespace Mehrak.Application.Services.Hsr.Memory;

internal class HsrMemoryApplicationService : BaseAttachmentApplicationService
{
    private readonly ICardService<HsrMemoryInformation> m_CardService;
    private readonly IImageUpdaterService m_ImageUpdaterService;
    private readonly IApiService<HsrMemoryInformation, BaseHoYoApiContext> m_ApiService;


    protected override string CommandName => "Memory of Chaos";
    protected override string CardName => "Memory of Chaos";
    public HsrMemoryApplicationService(
        ICardService<HsrMemoryInformation> cardService,
        IImageUpdaterService imageUpdaterService,
        IApiService<HsrMemoryInformation, BaseHoYoApiContext> apiService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        UserDbContext userContext,
        IAttachmentStorageService attachmentStorageService,
        ILogger<HsrMemoryApplicationService> logger)
        : base(gameRoleApi, userContext, attachmentStorageService, logger)
    {
        m_CardService = cardService;
        m_ImageUpdaterService = imageUpdaterService;
        m_ApiService = apiService;
    }

    protected override async Task<CommandResult> ExecuteCommandAsync(IApplicationContext context, CancellationToken cancellationToken = default)
    {
        var server = Enum.Parse<Server>(context.GetParameter("server")!);
        var region = server.ToRegion();

        var profileResult = await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken,
            Game.HonkaiStarRail, region, cancellationToken);
        if (!profileResult.IsSuccess)
        {
            if (profileResult.StatusCode == StatusCode.Cancelled)
                throw new OperationCanceledException(profileResult.ErrorMessage ?? "Cancelled");
            if (profileResult.StatusCode == StatusCode.Timeout)
                return CommandResult.Failure(CommandFailureReason.Timeout, ResponseMessage.TimeoutError);
            Logger.LogWarning(LogMessage.InvalidLogin, context.UserId);
            return CommandResult.Failure(CommandFailureReason.AuthError, ResponseMessage.AuthError);
        }
        var profile = profileResult.Data;

        await UpdateGameUidAsync(context.UserId, context.LtUid, Game.HonkaiStarRail, profile.GameUid, server.ToString(), cancellationToken);

        var gameUid = profile.GameUid;
        var memoryResult =
            await m_ApiService.GetAsync(new BaseHoYoApiContext(context.UserId, context.LtUid, context.LToken,
                gameUid, region), cancellationToken);
        if (!memoryResult.IsSuccess)
        {
            if (memoryResult.StatusCode == StatusCode.Cancelled)
                throw new OperationCanceledException(memoryResult.ErrorMessage ?? "Cancelled");
            if (memoryResult.StatusCode == StatusCode.Timeout)
                return CommandResult.Failure(CommandFailureReason.Timeout, ResponseMessage.TimeoutError);
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

        var tz = server.GetTimeZoneInfo();
        var startTime = new DateTimeOffset(memoryData.StartTime.ToDateTime(), tz.BaseUtcOffset)
            .ToUnixTimeSeconds();
        var endTime = new DateTimeOffset(memoryData.EndTime.ToDateTime(), tz.BaseUtcOffset)
            .ToUnixTimeSeconds();

        var fileName = GetFileName(JsonSerializer.Serialize(memoryData), "jpg", gameUid);
        if (await AttachmentExistsAsync(fileName))
        {
            return CommandResult.Success(
                [
                    new CommandText($"<@{context.UserId}>'s Memory of Chaos Summary", CommandText.TextType.Header3),
                        new CommandText($"Cycle start: <t:{startTime}:f>\nCycle end: <t:{endTime}:f>"),
                        new CommandAttachment(fileName),
                        new CommandText(ResponseMessage.ApiLimitationFooter, CommandText.TextType.Footer)
                ],
                true);
        }

        var tasks = memoryData.AllFloorDetail!.SelectMany(x => x.Node1.Avatars.Concat(x.Node2.Avatars))
            .DistinctBy(x => x.Id)
            .Select(x =>
                m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(), ImageProcessors.AvatarProcessor, cancellationToken));
        var completed = await Task.WhenAll(tasks);

        if (completed.Any(x => !x))
        {
            Logger.LogError(LogMessage.ImageUpdateError, "Memory of Chaos", context.UserId,
                JsonSerializer.Serialize(memoryData));
            return CommandResult.Failure(CommandFailureReason.ApiError, ResponseMessage.ImageUpdateError);
        }

        var cardContext = new BaseCardGenerationContext<HsrMemoryInformation>(context.UserId, memoryData, profile);
        cardContext.SetParameter("server", server);

        await using var card = await m_CardService.GetCardAsync(cardContext);

        if (!await StoreAttachmentAsync(context.UserId, fileName, card))
        {
            Logger.LogError(LogMessage.AttachmentStoreError, fileName, context.UserId);
            return CommandResult.Failure(CommandFailureReason.BotError, ResponseMessage.AttachmentStoreError);
        }

        return CommandResult.Success(
            [
                new CommandText($"<@{context.UserId}>'s Memory of Chaos Summary", CommandText.TextType.Header3),
                    new CommandText($"Cycle start: <t:{startTime}:f>\nCycle end: <t:{endTime}:f>"),
                    new CommandAttachment(fileName),
                    new CommandText(ResponseMessage.ApiLimitationFooter, CommandText.TextType.Footer)
            ],
            true);
    }
}
