#region

using System.Text.Json;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Services;
using Mehrak.Application.Shared.Services.Types;
using Mehrak.Application.Shared.Utility;
using Mehrak.Domain.Card;
using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Image;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.Domain.Shared.Utility;
using Mehrak.Domain.User.Models;
using Mehrak.GameApi.GameRole;
using Mehrak.GameApi.Hsr.Types;
using Mehrak.GameApi.Shared.Types;
using Mehrak.Infrastructure.User;

#endregion

namespace Mehrak.Application.Hsr.Memory;

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

        var (profile, memoryResult) = await FetchProfileAndPrimaryAsync(
            context.UserId, context.LtUid, context.LToken, Game.HonkaiStarRail, region,
            uid => m_ApiService.GetAsync(
                new BaseHoYoApiContext(context.UserId, context.LtUid, context.LToken, uid, region), cancellationToken),
            cancellationToken);

        var gameUid = profile.GameUid;
        if (!memoryResult.IsSuccess)
        {
            if (memoryResult.StatusCode == StatusCode.Cancelled)
                throw new OperationCanceledException(memoryResult.ErrorMessage ?? "Cancelled");
            if (memoryResult.StatusCode == StatusCode.Timeout)
                return CommandResult.Failure(CommandFailureReason.Timeout, CommandErrorCode.Timeout);
            Logger.LogError(LogMessage.ApiError, "Memory of Chaos", context.UserId, gameUid, memoryResult);
            return CommandResult.Failure(CommandFailureReason.ApiError,
                CommandErrorCode.ApiUnavailable, CommandErrorTarget.MemoryOfChaos);
        }

        var memoryData = memoryResult.Data;

        if (!memoryData.HasData || memoryData.BattleNum == 0)
        {
            Logger.LogInformation(LogMessage.NoClearRecords, "Memory of Chaos", context.UserId, gameUid);
            return CommandResult.Success(EndgameOutcome.Empty(Game.HonkaiStarRail, profile.GameUid, EndgameMode.MemoryOfChaos));
        }

        var tz = server.GetTimeZoneInfo();
        var startTime = new DateTimeOffset(memoryData.StartTime.ToDateTime(), tz.BaseUtcOffset)
            .ToUniversalTime();
        var endTime = new DateTimeOffset(memoryData.EndTime.ToDateTime(), tz.BaseUtcOffset)
            .ToUniversalTime();

        var fileName = GetCardFileName("hsr", "memory", "v1", memoryData, profile, new { Server = server });
        if (await AttachmentExistsAsync(fileName, cancellationToken))
        {
            return CommandResult.Success(EndgameOutcome.Card(Game.HonkaiStarRail, profile.GameUid, EndgameMode.MemoryOfChaos,
                StorageReference.Attachment(fileName), startTime, endTime));
        }

        var tasks = memoryData.AllFloorDetail!
            .SelectMany(x => (x.Node1?.Avatars ?? []).Concat(x.Node2?.Avatars ?? []).Concat(x.Node3?.Avatars ?? []))
            .DistinctBy(x => x.Id)
            .Select(x =>
                m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(), ImageProcessors.AvatarProcessor, cancellationToken));
        var completed = await Task.WhenAll(tasks);

        if (completed.Any(x => !x))
        {
            Logger.LogError(LogMessage.ImageUpdateError, "Memory of Chaos", context.UserId,
                JsonSerializer.Serialize(memoryData));
            return CommandResult.Failure(CommandFailureReason.ApiError, CommandErrorCode.ImageUpdateFailed, CommandErrorTarget.Image);
        }

        var cardContext = new BaseCardGenerationContext<HsrMemoryInformation>(context.UserId, memoryData, profile);
        cardContext.SetParameter("server", server);

        await using var card = await m_CardService.GetCardAsync(cardContext, cancellationToken);

        if (!await StoreAttachmentAsync(context.UserId, fileName, card, cancellationToken))
        {
            Logger.LogError(LogMessage.AttachmentStoreError, fileName, context.UserId);
            return CommandResult.Failure(CommandFailureReason.BotError, CommandErrorCode.AttachmentStoreFailed, CommandErrorTarget.Attachment);
        }

        return CommandResult.Success(EndgameOutcome.Card(Game.HonkaiStarRail, profile.GameUid, EndgameMode.MemoryOfChaos,
                StorageReference.Attachment(fileName), startTime, endTime));
    }
}
