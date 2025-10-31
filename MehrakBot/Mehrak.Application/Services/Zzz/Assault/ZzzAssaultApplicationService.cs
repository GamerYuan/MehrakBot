#region

using System.Text.Json;
using Mehrak.Application.Builders;
using Mehrak.Application.Models;
using Mehrak.Application.Services.Common;
using Mehrak.Application.Services.Genshin.Types;
using Mehrak.Application.Utility;
using Mehrak.Domain.Common;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.Domain.Utility;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Zzz.Types;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Services.Zzz.Assault;

internal class ZzzAssaultApplicationService : BaseApplicationService<ZzzAssaultApplicationContext>
{
    private readonly ICardService<ZzzAssaultData> m_CardService;
    private readonly IImageUpdaterService m_ImageUpdaterService;
    private readonly IApiService<ZzzAssaultData, BaseHoYoApiContext> m_ApiService;

    public ZzzAssaultApplicationService(
        ICardService<ZzzAssaultData> cardService,
        IImageUpdaterService imageUpdaterService,
        IApiService<ZzzAssaultData, BaseHoYoApiContext> apiService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        IUserRepository userRepository,
        ILogger<ZzzAssaultApplicationService> logger)
        : base(gameRoleApi, userRepository, logger)
    {
        m_CardService = cardService;
        m_ImageUpdaterService = imageUpdaterService;
        m_ApiService = apiService;
    }

    public override async Task<CommandResult> ExecuteAsync(ZzzAssaultApplicationContext context)
    {
        try
        {
            string region = context.Server.ToRegion();

            var profile = await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.ZenlessZoneZero,
                region);

            if (profile == null)
            {
                Logger.LogWarning(LogMessage.InvalidLogin, context.UserId);
                return CommandResult.Failure(CommandFailureReason.AuthError, ResponseMessage.AuthError);
            }

            await UpdateGameUidAsync(context.UserId, context.LtUid, Game.ZenlessZoneZero, profile.GameUid, context.Server);

            string gameUid = profile.GameUid;

            var assaultResponse =
                await m_ApiService.GetAsync(new BaseHoYoApiContext(context.UserId, context.LtUid, context.LToken,
                    gameUid, region));

            if (!assaultResponse.IsSuccess)
            {
                Logger.LogError(LogMessage.ApiError, "Assault", context.UserId, gameUid, assaultResponse);
                return CommandResult.Failure(CommandFailureReason.ApiError,
                    string.Format(ResponseMessage.ApiError, "Deadly Assault data"));
            }

            ZzzAssaultData assaultData = assaultResponse.Data;

            if (!assaultData.HasData || assaultData.List.Count == 0)
            {
                Logger.LogInformation(LogMessage.NoClearRecords, "Assault", context.UserId, gameUid);
                return CommandResult.Success(
                    [new CommandText(string.Format(ResponseMessage.NoClearRecords, "Deadly Assault"))],
                    isEphemeral: true);
            }

            IEnumerable<Task<bool>> avatarImageTask = assaultData.List.SelectMany(x => x.AvatarList)
                .DistinctBy(x => x.Id)
                .Select(avatar =>
                    m_ImageUpdaterService.UpdateImageAsync(avatar.ToImageData(), ImageProcessors.AvatarProcessor));
            IEnumerable<Task<bool>> buddyImageTask = assaultData.List.Select(x => x.Buddy)
                .Where(x => x is not null)
                .DistinctBy(x => x!.Id)
                .Select(buddy => m_ImageUpdaterService.UpdateImageAsync(buddy!.ToImageData(),
                    new ImageProcessorBuilder().Resize(300, 0).Build()));
            IEnumerable<Task<bool>> bossImageTask = assaultData.List
                .SelectMany(x => x.Boss)
                .Select(x => m_ImageUpdaterService.UpdateMultiImageAsync(x.ToImageData(),
                    GetBossImageProcessor()));
            IEnumerable<Task<bool>> buffImageTask = assaultData.List
                .SelectMany(x => x.Buff)
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

            var card = await m_CardService.GetCardAsync(
                new BaseCardGenerationContext<ZzzAssaultData>(context.UserId, assaultData, context.Server, profile));

            TimeZoneInfo tz = context.Server.GetTimeZoneInfo();

            return CommandResult.Success([
                    new CommandText($"<@{context.UserId}>'s Deadly Assault Summary", CommandText.TextType.Header3),
                    new CommandText($"Cycle start: <t:{assaultData.StartTime.ToTimestamp(tz)}:f>\n" +
                                    $"Cycle end: <t:{assaultData.EndTime.ToTimestamp(tz)}:f>"),
                    new CommandAttachment("da_card.jpg", card),
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
            Image background = images[0];
            Image icon = images[1];
            background.Mutate(ctx =>
            {
                ctx.DrawImage(icon, new Point(0, 0), 1f);
                ctx.Resize(0, BossImageHeight);
                Size size = ctx.GetCurrentSize();
                IPath border = ImageUtility.CreateRoundedRectanglePath(size.Width, BossImageHeight, 15);
                ctx.Draw(Color.Black, 4f, border);
                ctx.ApplyRoundedCorners(15);
            });
        });

        return processor;
    }
}
