#region

using System.Text.Json;
using Mehrak.Application.Builders;
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
using Mehrak.Domain.User.Models;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Zzz.Types;
using Mehrak.Infrastructure.Context;

#endregion

namespace Mehrak.Application.Services.Zzz.Defense;

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

        var profileResult = await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.ZenlessZoneZero,
            region, cancellationToken);
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

        await UpdateGameUidAsync(context.UserId, context.LtUid, Game.ZenlessZoneZero, profile.GameUid, server.ToString(), cancellationToken);

        var gameUid = profile.GameUid;

        var defenseResponse =
            await m_ApiService.GetAsync(new BaseHoYoApiContext(context.UserId, context.LtUid, context.LToken,
                gameUid, region), cancellationToken);

        if (!defenseResponse.IsSuccess)
        {
            if (defenseResponse.StatusCode == StatusCode.Cancelled)
                throw new OperationCanceledException(defenseResponse.ErrorMessage ?? "Cancelled");
            if (defenseResponse.StatusCode == StatusCode.Timeout)
                return CommandResult.Failure(CommandFailureReason.Timeout, ResponseMessage.TimeoutError);
            Logger.LogError(LogMessage.ApiError, "Defense", context.UserId, gameUid, defenseResponse);
            return CommandResult.Failure(CommandFailureReason.ApiError,
                string.Format(ResponseMessage.ApiError, "Shiyu Defense data"));
        }

        var defenseData = defenseResponse.Data!;

        if (defenseData.Brief == null)
        {
            Logger.LogInformation(LogMessage.NoClearRecords, "Shiyu Defense", context.UserId, gameUid);
            return CommandResult.Success(
                [new CommandText(string.Format(ResponseMessage.NoClearRecords, "Shiyu Defense"))],
                isEphemeral: true);
        }

        var nonNull = defenseData.FifthLayerDetail?.LayerChallengeInfoList;
        if (nonNull == null || nonNull.Count == 0)
        {
            Logger.LogInformation(LogMessage.NoClearRecords, "Shiyu Defense", context.UserId, gameUid);
            return CommandResult.Success(
                [new CommandText(string.Format(ResponseMessage.NoClearRecords, "Shiyu Defense"))],
                isEphemeral: true);
        }

        var fileName = GetFileName(JsonSerializer.Serialize(defenseData), "jpg", gameUid);
        if (await AttachmentExistsAsync(fileName))
        {
            return CommandResult.Success([
                    new CommandText($"<@{context.UserId}>'s Shiyu Defense Summary", CommandText.TextType.Header3),
                        new CommandText(
                            $"Cycle start: <t:{defenseData.BeginTime}:f>\nCycle end: <t:{defenseData.EndTime}:f>"),
                        new CommandAttachment(fileName),
                        new CommandText(ResponseMessage.ApiLimitationFooter, CommandText.TextType.Footer)
                ],
                true);
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
                new ImageProcessorBuilder().Resize(250, 0).AddOperation(x => x.ApplyGradientFade()).Build(), cancellationToken));

        var completed = await Task.WhenAll(updateImageTask.Concat(updateBuddyTask).Concat(bossTask));

        if (completed.Any(x => !x))
        {
            Logger.LogError(LogMessage.ImageUpdateError, "Shiyu Defense", context.UserId, gameUid);
            return CommandResult.Failure(CommandFailureReason.ApiError, ResponseMessage.ImageUpdateError);
        }

        var cardContext = new BaseCardGenerationContext<ZzzDefenseDataV2>(context.UserId, defenseData, profile);
        cardContext.SetParameter("server", server);

        await using var card = await m_CardService.GetCardAsync(cardContext);

        if (!await StoreAttachmentAsync(context.UserId, fileName, card))
        {
            Logger.LogError(LogMessage.AttachmentStoreError, fileName, context.UserId);
            return CommandResult.Failure(CommandFailureReason.BotError, ResponseMessage.AttachmentStoreError);
        }

        return CommandResult.Success([
                new CommandText($"<@{context.UserId}>'s Shiyu Defense Summary", CommandText.TextType.Header3),
                    new CommandText(
                        $"Cycle start: <t:{defenseData.BeginTime}:f>\nCycle end: <t:{defenseData.EndTime}:f>"),
                    new CommandAttachment(fileName),
                    new CommandText(ResponseMessage.ApiLimitationFooter, CommandText.TextType.Footer)
            ],
            true);
    }
}
