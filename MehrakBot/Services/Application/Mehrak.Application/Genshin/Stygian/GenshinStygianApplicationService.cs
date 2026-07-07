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
using Mehrak.Domain.User.Models;
using Mehrak.GameApi.GameRole;
using Mehrak.GameApi.Genshin.Types;
using Mehrak.GameApi.Shared.Types;
using Mehrak.Infrastructure.User;

#endregion

namespace Mehrak.Application.Genshin.Stygian;

public class GenshinStygianApplicationService : BaseAttachmentApplicationService
{
    private readonly IImageUpdaterService m_ImageUpdaterService;
    private readonly ICardService<StygianData> m_CardService;
    private readonly IApiService<GenshinStygianInformation, BaseHoYoApiContext> m_ApiService;


    protected override string CommandName => "Stygian";
    protected override string CardName => "Stygian Onslaught";
    public GenshinStygianApplicationService(
        IImageUpdaterService imageUpdaterService,
        ICardService<StygianData> cardService,
        IApiService<GenshinStygianInformation, BaseHoYoApiContext> apiService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        UserDbContext userContext,
        IAttachmentStorageService attachmentStorageService,
        ILogger<GenshinStygianApplicationService> logger)
        : base(gameRoleApi, userContext, attachmentStorageService, logger)
    {
        m_ImageUpdaterService = imageUpdaterService;
        m_CardService = cardService;
        m_ApiService = apiService;
    }

    protected override async Task<CommandResult> ExecuteCommandAsync(IApplicationContext context, CancellationToken cancellationToken = default)
    {
        var server = Enum.Parse<Server>(context.GetParameter("server")!);
        var region = server.ToRegion();

        var (profile, stygianInfo) = await FetchProfileAndPrimaryAsync(
            context.UserId, context.LtUid, context.LToken, Game.Genshin, region,
            uid => m_ApiService.GetAsync(new BaseHoYoApiContext(context.UserId, context.LtUid, context.LToken,
                uid, region), cancellationToken),
            cancellationToken);

        var gameUid = profile.GameUid;
        if (!stygianInfo.IsSuccess)
        {
            if (stygianInfo.StatusCode == StatusCode.Cancelled)
                throw new OperationCanceledException(stygianInfo.ErrorMessage ?? "Cancelled");
            if (stygianInfo.StatusCode == StatusCode.Timeout)
                return CommandResult.Failure(CommandFailureReason.Timeout, ResponseMessage.TimeoutError);
            Logger.LogError(LogMessage.ApiError, "Stygian", context.UserId, gameUid, stygianInfo);
            return CommandResult.Failure(CommandFailureReason.ApiError,
                string.Format(ResponseMessage.ApiError, "Stygian Onslaught data"));
        }

        if (!stygianInfo.Data.IsUnlock)
        {
            Logger.LogInformation("Stygian Onslaught is not unlocked for User {UserId} UID {GameUid}",
                context.UserId, gameUid);
            return CommandResult.Success([new CommandText("Stygian Onslaught is not unlocked")], isEphemeral: true);
        }

        if (!stygianInfo.Data.Data![0].Single.HasData)
        {
            Logger.LogInformation(LogMessage.NoClearRecords, "Stygian", context.UserId, gameUid);
            return CommandResult.Success(
                [new CommandText(string.Format(ResponseMessage.NoClearRecords, "Stygian Onslaught"))],
                isEphemeral: true);
        }

        var stygianData = stygianInfo.Data.Data[0].Single;

        var filename = GetFileName(JsonSerializer.Serialize(stygianData), "jpg", profile.GameUid);
        if (await AttachmentExistsAsync(filename))
        {
            return CommandResult.Success(
                [new CommandText($"<@{context.UserId}>'s Stygian Onslaught Summary",
                        CommandText.TextType.Header3),
                     new CommandText(
                         $"Cycle start: <t:{stygianInfo.Data.Data[0].Schedule!.StartTime}:f>\n" +
                         $"Cycle end: <t:{stygianInfo.Data.Data[0].Schedule!.EndTime}:f>"),
                     new CommandAttachment(filename),
                     new CommandText(ResponseMessage.ApiLimitationFooter, CommandText.TextType.Footer)
                ], true);
        }

        var avatarTasks = stygianData.Challenge!.SelectMany(x => x.Teams).Select(x =>
            m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(), ImageProcessors.AvatarProcessor, cancellationToken));
        var sideAvatarTasks = stygianData.Challenge!.SelectMany(x => x.BestAvatar).Select(x =>
            m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(),
                new ImageProcessorBuilder().Resize(0, 150).Build(), cancellationToken));
        var monsterImageTask = stygianData.Challenge!.Select(x => x.Monster)
            .Select(x =>
                m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(), new ImageProcessorBuilder().Build(), cancellationToken));

        var completed = await Task.WhenAll(avatarTasks.Concat(sideAvatarTasks).Concat(monsterImageTask));

        if (completed.Any(x => !x))
        {
            Logger.LogError(LogMessage.ImageUpdateError, "Stygian", context.UserId,
                JsonSerializer.Serialize(stygianData));
            return CommandResult.Failure(CommandFailureReason.ApiError, ResponseMessage.ImageUpdateError);
        }

        var cardContext = new BaseCardGenerationContext<StygianData>(context.UserId,
            stygianInfo.Data.Data[0], profile);
        cardContext.SetParameter("server", server);

        using var card = await m_CardService.GetCardAsync(cardContext);
        if (!await StoreAttachmentAsync(context.UserId, filename, card))
        {
            Logger.LogError(LogMessage.AttachmentStoreError, filename, context.UserId);
            return CommandResult.Failure(CommandFailureReason.BotError,
                ResponseMessage.AttachmentStoreError);
        }

        return CommandResult.Success([
                new CommandText($"<@{context.UserId}>'s Stygian Onslaught Summary", CommandText.TextType.Header3),
                    new CommandText($"Cycle start: <t:{stygianInfo.Data.Data[0].Schedule!.StartTime}:f>\n" +
                                    $"Cycle end: <t:{stygianInfo.Data.Data[0].Schedule!.EndTime}:f>"),
                    new CommandAttachment(filename),
                    new CommandText(ResponseMessage.ApiLimitationFooter, CommandText.TextType.Footer)
            ],
            true);
    }
}
