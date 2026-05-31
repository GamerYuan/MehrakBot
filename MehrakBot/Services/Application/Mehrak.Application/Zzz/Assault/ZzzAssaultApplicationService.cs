#region

using System.Text.Json;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Builders;
using Mehrak.Application.Shared.Models;
using Mehrak.Application.Shared.Services;
using Mehrak.Application.Shared.Services.Types;
using Mehrak.Application.Shared.Utility;
using Mehrak.Domain.Card;
using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Image;
using Mehrak.Domain.Image.Abstractions;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.Domain.Shared.Utility;
using Mehrak.Domain.User.Models;
using Mehrak.GameApi.GameRole;
using Mehrak.GameApi.Shared.Types;
using Mehrak.GameApi.Zzz.Types;
using Mehrak.Infrastructure.User;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Zzz.Assault;

internal class ZzzAssaultApplicationService : BaseAttachmentApplicationService
{
    private readonly ICardService<ZzzAssaultData> m_CardService;
    private readonly IImageUpdaterService m_ImageUpdaterService;
    private readonly IApiService<ZzzAssaultData, BaseHoYoApiContext> m_ApiService;


    protected override string CommandName => "Assault";
    protected override string CardName => "Deadly Assault";
    public ZzzAssaultApplicationService(
        ICardService<ZzzAssaultData> cardService,
        IImageUpdaterService imageUpdaterService,
        IApiService<ZzzAssaultData, BaseHoYoApiContext> apiService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        UserDbContext userContext,
        IAttachmentStorageService attachmentStorageService,
        ILogger<ZzzAssaultApplicationService> logger)
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

        var assaultResponse =
            await m_ApiService.GetAsync(new BaseHoYoApiContext(context.UserId, context.LtUid, context.LToken,
                gameUid, region), cancellationToken);

        if (!assaultResponse.IsSuccess)
        {
            if (assaultResponse.StatusCode == StatusCode.Cancelled)
                throw new OperationCanceledException(assaultResponse.ErrorMessage ?? "Cancelled");
            if (assaultResponse.StatusCode == StatusCode.Timeout)
                return CommandResult.Failure(CommandFailureReason.Timeout, ResponseMessage.TimeoutError);
            Logger.LogError(LogMessage.ApiError, "Assault", context.UserId, gameUid, assaultResponse);
            return CommandResult.Failure(CommandFailureReason.ApiError,
                string.Format(ResponseMessage.ApiError, "Deadly Assault data"));
        }

        var assaultData = assaultResponse.Data;

        if (!assaultData.HasData || assaultData.List.Count == 0)
        {
            Logger.LogInformation(LogMessage.NoClearRecords, "Assault", context.UserId, gameUid);
            return CommandResult.Success(
                [new CommandText(string.Format(ResponseMessage.NoClearRecords, "Deadly Assault"))],
                isEphemeral: true);
        }

        var tz = server.GetTimeZoneInfo();
        var startTs = assaultData.StartTime.ToTimestamp(tz);
        var endTs = assaultData.EndTime.ToTimestamp(tz);

        var fileName = GetFileName(JsonSerializer.Serialize(assaultData), "jpg", gameUid);
        if (await AttachmentExistsAsync(fileName))
        {
            return CommandResult.Success([
                    new CommandText($"<@{context.UserId}>'s Deadly Assault Summary", CommandText.TextType.Header3),
                        new CommandText($"Cycle start: <t:{startTs}:f>\n" +
                                        $"Cycle end: <t:{endTs}:f>"),
                        new CommandAttachment(fileName),
                        new CommandText(ResponseMessage.ApiLimitationFooter, CommandText.TextType.Footer)
                ],
                true);
        }

        var avatarImageTask = assaultData.List.SelectMany(x => x.AvatarList)
            .DistinctBy(x => x.Id)
            .Select(avatar =>
                m_ImageUpdaterService.UpdateImageAsync(avatar.ToImageData(), ImageProcessors.AvatarProcessor, cancellationToken));
        var buddyImageTask = assaultData.List.Select(x => x.Buddy)
            .Where(x => x is not null)
            .DistinctBy(x => x!.Id)
            .Select(buddy => m_ImageUpdaterService.UpdateImageAsync(buddy!.ToImageData(),
                new ImageProcessorBuilder().Resize(300, 0).Build(), cancellationToken));
        var bossImageTask = assaultData.List
            .SelectMany(x => x.Boss)
            .Select(x => m_ImageUpdaterService.UpdateMultiImageAsync(x.ToImageData(),
                GetBossImageProcessor(), cancellationToken));
        var buffImageTask = assaultData.List
            .SelectMany(x => x.Buff)
            .DistinctBy(x => x.Name)
            .Select(x => m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(),
                new ImageProcessorBuilder().Resize(80, 0).Build(), cancellationToken));

        var completed =
            await Task.WhenAll(avatarImageTask.Concat(buddyImageTask).Concat(bossImageTask).Concat(buffImageTask));

        if (completed.Any(x => !x))
        {
            Logger.LogError(LogMessage.ImageUpdateError, "Assault", context.UserId,
                JsonSerializer.Serialize(assaultData));
            return CommandResult.Failure(CommandFailureReason.ApiError, ResponseMessage.ImageUpdateError);
        }

        var cardContext = new BaseCardGenerationContext<ZzzAssaultData>(context.UserId, assaultData, profile);
        cardContext.SetParameter("server", server);

        await using var card = await m_CardService.GetCardAsync(cardContext);

        if (!await StoreAttachmentAsync(context.UserId, fileName, card))
        {
            Logger.LogError(LogMessage.AttachmentStoreError, fileName, context.UserId);
            return CommandResult.Failure(CommandFailureReason.BotError, ResponseMessage.AttachmentStoreError);
        }

        return CommandResult.Success([
                new CommandText($"<@{context.UserId}>'s Deadly Assault Summary", CommandText.TextType.Header3),
                    new CommandText($"Cycle start: <t:{startTs}:f>\n" +
                                    $"Cycle end: <t:{endTs}:f>"),
                    new CommandAttachment(fileName),
                    new CommandText(ResponseMessage.ApiLimitationFooter, CommandText.TextType.Footer)
            ],
            true);
    }

    private static IMultiImageProcessor GetBossImageProcessor()
    {
        var processor = new MultiImageProcessorBase();
        processor.SetOperation(images =>
        {
            const int BossImageHeight = 230;
            var background = images[0];
            var icon = images[1];
            background.Mutate(ctx =>
            {
                ctx.DrawImage(icon, new Point(0, 0), 1f);
                ctx.Resize(0, BossImageHeight);
                var size = ctx.GetCurrentSize();
                var border = ImageUtility.CreateRoundedRectanglePath(size.Width, BossImageHeight, 15);
                ctx.Draw(Color.Black, 4f, border);
                ctx.ApplyRoundedCorners(15);
            });
        });

        return processor;
    }
}
