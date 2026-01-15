#region

using System.Text.Json;
using Mehrak.Application.Builders;
using Mehrak.Application.Models;
using Mehrak.Application.Services.Common;
using Mehrak.Application.Services.Common.Types;
using Mehrak.Application.Utility;
using Mehrak.Domain.Common;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.Domain.Utility;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Zzz.Types;
using Mehrak.Infrastructure.Context;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Services.Zzz.Assault;

internal class ZzzAssaultApplicationService : BaseAttachmentApplicationService<ZzzAssaultApplicationContext>
{
    private readonly ICardService<ZzzAssaultData> m_CardService;
    private readonly IImageUpdaterService m_ImageUpdaterService;
    private readonly IApiService<ZzzAssaultData, BaseHoYoApiContext> m_ApiService;

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

    public override async Task<CommandResult> ExecuteAsync(ZzzAssaultApplicationContext context)
    {
        try
        {
            var server = Enum.Parse<Server>(context.GetParameter("server")!);
            var region = server.ToRegion();

            var profile = await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.ZenlessZoneZero,
                region);

            if (profile == null)
            {
                Logger.LogWarning(LogMessage.InvalidLogin, context.UserId);
                return CommandResult.Failure(CommandFailureReason.AuthError, ResponseMessage.AuthError);
            }

            await UpdateGameUidAsync(context.UserId, context.LtUid, Game.ZenlessZoneZero, profile.GameUid, server.ToString());

            var gameUid = profile.GameUid;

            var assaultResponse =
                await m_ApiService.GetAsync(new BaseHoYoApiContext(context.UserId, context.LtUid, context.LToken,
                    gameUid, region));

            if (!assaultResponse.IsSuccess)
            {
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
                    m_ImageUpdaterService.UpdateImageAsync(avatar.ToImageData(), ImageProcessors.AvatarProcessor));
            var buddyImageTask = assaultData.List.Select(x => x.Buddy)
                .Where(x => x is not null)
                .DistinctBy(x => x!.Id)
                .Select(buddy => m_ImageUpdaterService.UpdateImageAsync(buddy!.ToImageData(),
                    new ImageProcessorBuilder().Resize(300, 0).Build()));
            var bossImageTask = assaultData.List
                .SelectMany(x => x.Boss)
                .Select(x => m_ImageUpdaterService.UpdateMultiImageAsync(x.ToImageData(),
                    GetBossImageProcessor()));
            var buffImageTask = assaultData.List
                .SelectMany(x => x.Buff)
                .DistinctBy(x => x.Name)
                .Select(x => m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(),
                    new ImageProcessorBuilder().Build()));

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
        catch (CommandException e)
        {
            Logger.LogError(e, LogMessage.UnknownError, "Assault", context.UserId, e.Message);
            return CommandResult.Failure(CommandFailureReason.BotError,
                string.Format(ResponseMessage.CardGenError, "Deadly Assault"));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, LogMessage.UnknownError, "Assault", context.UserId, ex.Message);
            return CommandResult.Failure(CommandFailureReason.Unknown, ResponseMessage.UnknownError);
        }
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
