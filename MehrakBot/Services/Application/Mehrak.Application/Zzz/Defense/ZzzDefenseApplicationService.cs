#region

using System.Text.Json;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Builders;
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
using Mehrak.GameApi.Shared.Types;
using Mehrak.GameApi.Zzz.Types;
using Mehrak.Infrastructure.User;

#endregion

namespace Mehrak.Application.Zzz.Defense;

internal class ZzzDefenseApplicationService : BaseAttachmentApplicationService
{
    private readonly ICardService<ZzzDefenseDataV2> m_CardService;
    private readonly IImageUpdaterService m_ImageUpdaterService;
    private readonly IApiService<ZzzDefenseDataV2, BaseHoYoApiContext> m_ApiService;


    protected override string CommandName => "Defense";
    protected override string CardName => "Shiyu Defense";
    public ZzzDefenseApplicationService(
        ICardService<ZzzDefenseDataV2> cardService,
        IImageUpdaterService imageUpdaterService,
        IApiService<ZzzDefenseDataV2, BaseHoYoApiContext> apiService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        UserDbContext userContext,
        IAttachmentStorageService attachmentStorageService,
        ILogger<ZzzDefenseApplicationService> logger)
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

        var (profile, defenseResponse) = await FetchProfileAndPrimaryAsync(
            context.UserId, context.LtUid, context.LToken, Game.ZenlessZoneZero, region,
            uid => m_ApiService.GetAsync(
                new BaseHoYoApiContext(context.UserId, context.LtUid, context.LToken, uid, region), cancellationToken),
            cancellationToken);

        var gameUid = profile.GameUid;

        if (!defenseResponse.IsSuccess)
        {
            if (defenseResponse.StatusCode == StatusCode.Cancelled)
                throw new OperationCanceledException(defenseResponse.ErrorMessage ?? "Cancelled");
            if (defenseResponse.StatusCode == StatusCode.Timeout)
                return CommandResult.Failure(CommandFailureReason.Timeout, CommandErrorCode.Timeout);
            Logger.LogError(LogMessage.ApiError, "Defense", context.UserId, gameUid, defenseResponse);
            return CommandResult.Failure(CommandFailureReason.ApiError,
                CommandErrorCode.ApiUnavailable, CommandErrorTarget.ShiyuDefense);
        }

        var defenseData = defenseResponse.Data!;

        if (defenseData.Brief == null)
        {
            Logger.LogInformation(LogMessage.NoClearRecords, "Shiyu Defense", context.UserId, gameUid);
            return CommandResult.Success(EndgameOutcome.Empty(Game.ZenlessZoneZero, profile.GameUid, EndgameMode.ShiyuDefense));
        }

        var nonNull = defenseData.FifthLayerDetail?.LayerChallengeInfoList;
        if (nonNull == null || nonNull.Count == 0)
        {
            Logger.LogInformation(LogMessage.NoClearRecords, "Shiyu Defense", context.UserId, gameUid);
            return CommandResult.Success(EndgameOutcome.Empty(Game.ZenlessZoneZero, profile.GameUid, EndgameMode.ShiyuDefense));
        }

        var fileName = GetCardFileName("zzz", "defense", "v1", defenseData, profile, new { Server = server });
        if (await AttachmentExistsAsync(fileName, cancellationToken))
        {
            return CommandResult.Success(EndgameOutcome.Card(Game.ZenlessZoneZero, profile.GameUid, EndgameMode.ShiyuDefense,
                StorageReference.Attachment(fileName), DateTimeOffset.FromUnixTimeSeconds(long.Parse(defenseData.BeginTime!)), DateTimeOffset.FromUnixTimeSeconds(long.Parse(defenseData.EndTime!))));
        }

        var updateImageTask = nonNull.SelectMany(x => x.AvatarList)
            .DistinctBy(x => x!.Id)
            .Select(avatar =>
                m_ImageUpdaterService.UpdateImageAsync(avatar.ToImageData(), ImageProcessors.AvatarProcessor, cancellationToken));
        var updateBuddyTask = nonNull
            .Select(x => x.Buddy)
            .Where(x => x is not null)
            .DistinctBy(x => x!.Id)
            .Select(buddy => m_ImageUpdaterService.UpdateImageAsync(buddy!.ToImageData(),
                new ImageProcessorBuilder().Resize(300, 0).Build(), cancellationToken));
        var bossTask = defenseData.FifthLayerDetail!.LayerChallengeInfoList
            .Select(x => m_ImageUpdaterService.UpdateImageAsync(x.ToMonsterImageData(),
                new ImageProcessorBuilder().Resize(250, 0).AddOperation(x => x.ApplyGradientFade(easing: EasingType.InQuint)).Build(), cancellationToken));

        var completed = await Task.WhenAll(updateImageTask.Concat(updateBuddyTask).Concat(bossTask));

        if (completed.Any(x => !x))
        {
            Logger.LogError(LogMessage.ImageUpdateError, "Shiyu Defense", context.UserId, gameUid);
            return CommandResult.Failure(CommandFailureReason.ApiError, CommandErrorCode.ImageUpdateFailed, CommandErrorTarget.Image);
        }

        var cardContext = new BaseCardGenerationContext<ZzzDefenseDataV2>(context.UserId, defenseData, profile);
        cardContext.SetParameter("server", server);

        await using var card = await m_CardService.GetCardAsync(cardContext, cancellationToken);

        if (!await StoreAttachmentAsync(context.UserId, fileName, card, cancellationToken))
        {
            Logger.LogError(LogMessage.AttachmentStoreError, fileName, context.UserId);
            return CommandResult.Failure(CommandFailureReason.BotError, CommandErrorCode.AttachmentStoreFailed, CommandErrorTarget.Attachment);
        }

        return CommandResult.Success(EndgameOutcome.Card(Game.ZenlessZoneZero, profile.GameUid, EndgameMode.ShiyuDefense,
                StorageReference.Attachment(fileName), DateTimeOffset.FromUnixTimeSeconds(long.Parse(defenseData.BeginTime!)), DateTimeOffset.FromUnixTimeSeconds(long.Parse(defenseData.EndTime!))));
    }
}
