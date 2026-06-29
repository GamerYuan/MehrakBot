#region

using System.Text.Json;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Builders;
using Mehrak.Application.Shared.Renderers.Extensions;
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
using Mehrak.Infrastructure.User;

#endregion

namespace Mehrak.Application.Hsr.EndGame;

public class HsrEndGameApplicationService : BaseAttachmentApplicationService
{
    private readonly IServiceProvider m_ServiceProvider;
    private readonly IImageUpdaterService m_ImageUpdaterService;
    private readonly IApiService<HsrEndInformation, HsrEndGameApiContext> m_ApiService;

    protected override string CommandName => "End Game";
    protected override string CardName => Mode switch
    {
        HsrEndGameMode.PureFiction => "Pure Fiction",
        HsrEndGameMode.ApocalypticShadow => "Apocalyptic Shadow",
        _ => throw new ArgumentOutOfRangeException()
    };

    private HsrEndGameMode Mode { get; set; }

    public HsrEndGameApplicationService(
        IServiceProvider serviceProvider,
        IImageUpdaterService imageUpdaterService,
        IApiService<HsrEndInformation, HsrEndGameApiContext> apiService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        UserDbContext userContext,
        IAttachmentStorageService attachmentStorageService,
        ILogger<HsrEndGameApplicationService> logger) : base(gameRoleApi, userContext, attachmentStorageService, logger)
    {
        m_ServiceProvider = serviceProvider;
        m_ImageUpdaterService = imageUpdaterService;
        m_ApiService = apiService;
    }

    protected override async Task<CommandResult> ExecuteCommandAsync(IApplicationContext context, CancellationToken cancellationToken = default)
    {
        var server = Enum.Parse<Server>(context.GetParameter("server")!);
        var mode = Enum.Parse<HsrEndGameMode>(context.GetParameter("mode")!);
        Mode = mode;
        var region = server.ToRegion();

        var cardService = m_ServiceProvider.GetRequiredKeyedService<ICardService<HsrEndInformation>>(mode);

        var profileResult = await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.HonkaiStarRail,
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

        _ = UpdateGameUidAsync(context.UserId, context.LtUid, Game.HonkaiStarRail, profile.GameUid, server.ToString(), cancellationToken);

        var challengeResponse = await m_ApiService.GetAsync(
            new HsrEndGameApiContext(context.UserId, context.LtUid, context.LToken, profile.GameUid, region,
                mode), cancellationToken);
        if (!challengeResponse.IsSuccess)
        {
            if (challengeResponse.StatusCode == StatusCode.Cancelled)
                throw new OperationCanceledException(challengeResponse.ErrorMessage ?? "Cancelled");
            if (challengeResponse.StatusCode == StatusCode.Timeout)
                return CommandResult.Failure(CommandFailureReason.Timeout, ResponseMessage.TimeoutError);
            Logger.LogError(LogMessage.ApiError, mode.GetString(), context.UserId, profile.GameUid,
                challengeResponse);
            return CommandResult.Failure(CommandFailureReason.ApiError,
                string.Format(ResponseMessage.ApiError, $"{mode.GetString()} data"));
        }

        var challengeData = challengeResponse.Data;
        if (!challengeData.HasData)
        {
            Logger.LogInformation(LogMessage.NoClearRecords, mode.GetString(), context.UserId,
                profile.GameUid);
            return CommandResult.Success(
                [new CommandText(string.Format(ResponseMessage.NoClearRecords, mode.GetString()))],
                isEphemeral: true);
        }

        if (challengeData.AllFloorDetail.All(x => x is { Node1: null, Node2: null, Node3: null }))
        {
            Logger.LogInformation(LogMessage.NoClearRecords, mode.GetString(), context.UserId,
                profile.GameUid);
            return CommandResult.Success(
                [new CommandText(string.Format(ResponseMessage.NoClearRecords, mode.GetString()))],
                isEphemeral: true);
        }

        var tz = server.GetTimeZoneInfo();
        var group = challengeData.Groups[0];
        var startTime = new DateTimeOffset(group.BeginTime.ToDateTime(), tz.BaseUtcOffset)
            .ToUnixTimeSeconds();
        var endTime = new DateTimeOffset(group.EndTime.ToDateTime(), tz.BaseUtcOffset)
            .ToUnixTimeSeconds();

        var fileName = GetFileName(JsonSerializer.Serialize(challengeData), "jpg", profile.GameUid);
        if (await AttachmentExistsAsync(fileName))
        {
            return CommandResult.Success([
                    new CommandText($"<@{context.UserId}>'s {mode.GetString()} Summary",
                            CommandText.TextType.Header3),
                        new CommandText($"Cycle start: <t:{startTime}:f>\nCycle end: <t:{endTime}:f>"),
                        new CommandAttachment(fileName),
                        new CommandText(ResponseMessage.ApiLimitationFooter, CommandText.TextType.Footer)
                ],
                true);
        }

        var tasks = challengeData.AllFloorDetail
            .Where(x => !x.IsFast)
            .SelectMany(x => (x.Node1?.Avatars ?? []).Concat(x.Node2?.Avatars ?? []).Concat(x.Node3?.Avatars ?? []))
            .DistinctBy(x => x.Id)
            .Select(x => m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(), ImageProcessors.AvatarProcessor, cancellationToken));
        var buffTasks = challengeData.AllFloorDetail.Where(x => !x.IsFast)
            .SelectMany(x => new HsrEndBuff?[] { x.Node1?.Buff, x.Node2?.Buff, x.Node3?.Buff })
            .OfType<HsrEndBuff>()
            .DistinctBy(x => x.Id)
            .Select(x =>
                m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(),
                    new ImageProcessorBuilder().AddOperation(x => x.CropTransparentPixels()).Build(), cancellationToken));

        var completed = await Task.WhenAll(tasks.Concat(buffTasks));

        if (completed.Any(x => !x))
        {
            Logger.LogError(LogMessage.ImageUpdateError, mode.GetString(), context.UserId,
                JsonSerializer.Serialize(challengeData));
            return CommandResult.Failure(CommandFailureReason.ApiError, ResponseMessage.ImageUpdateError);
        }

        var cardContext = new BaseCardGenerationContext<HsrEndInformation>(
            context.UserId, challengeData, profile);
        cardContext.SetParameter("server", server);
        cardContext.SetParameter("mode", mode);

        await using var card = await cardService.GetCardAsync(cardContext);

        if (!await StoreAttachmentAsync(context.UserId, fileName, card))
        {
            Logger.LogError(LogMessage.AttachmentStoreError, fileName, context.UserId);
            return CommandResult.Failure(CommandFailureReason.BotError,
                ResponseMessage.AttachmentStoreError);
        }

        return CommandResult.Success([
                new CommandText($"<@{context.UserId}>'s {mode.GetString()} Summary",
                        CommandText.TextType.Header3),
                    new CommandText($"Cycle start: <t:{startTime}:f>\nCycle end: <t:{endTime}:f>"),
                    new CommandAttachment(fileName),
                    new CommandText(ResponseMessage.ApiLimitationFooter, CommandText.TextType.Footer)
            ],
            true
        );
    }
}
