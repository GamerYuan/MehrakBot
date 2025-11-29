#region

using Mehrak.Application.Builders;
using Mehrak.Application.Services.Common;
using Mehrak.Application.Services.Common.Types;
using Mehrak.Application.Utility;
using Mehrak.Domain.Common;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Zzz.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.Application.Services.Zzz.Defense;

internal class ZzzDefenseApplicationService : BaseApplicationService<ZzzDefenseApplicationContext>
{
    private readonly ICardService<ZzzDefenseData> m_CardService;
    private readonly IImageUpdaterService m_ImageUpdaterService;
    private readonly IApiService<ZzzDefenseData, BaseHoYoApiContext> m_ApiService;

    public ZzzDefenseApplicationService(
        ICardService<ZzzDefenseData> cardService,
        IImageUpdaterService imageUpdaterService,
        IApiService<ZzzDefenseData, BaseHoYoApiContext> apiService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        IUserRepository userRepository,
        ILogger<ZzzDefenseApplicationService> logger)
        : base(gameRoleApi, userRepository, logger)
    {
        m_CardService = cardService;
        m_ImageUpdaterService = imageUpdaterService;
        m_ApiService = apiService;
    }

    public override async Task<CommandResult> ExecuteAsync(ZzzDefenseApplicationContext context)
    {
        try
        {
            Server server = Enum.Parse<Server>(context.GetParameter<string>("server")!);
            var region = server.ToRegion();

            GameProfileDto? profile = await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.ZenlessZoneZero,
                region);

            if (profile == null)
            {
                Logger.LogWarning(LogMessage.InvalidLogin, context.UserId);
                return CommandResult.Failure(CommandFailureReason.AuthError, ResponseMessage.AuthError);
            }

            await UpdateGameUidAsync(context.UserId, context.LtUid, Game.ZenlessZoneZero, profile.GameUid, server);

            var gameUid = profile.GameUid;

            Result<ZzzDefenseData> defenseResponse =
                await m_ApiService.GetAsync(new BaseHoYoApiContext(context.UserId, context.LtUid, context.LToken,
                    gameUid, region));

            if (!defenseResponse.IsSuccess)
            {
                Logger.LogError(LogMessage.ApiError, "Defense", context.UserId, gameUid, defenseResponse);
                return CommandResult.Failure(CommandFailureReason.ApiError,
                    string.Format(ResponseMessage.ApiError, "Shiyu Defense data"));
            }

            ZzzDefenseData defenseData = defenseResponse.Data!;

            if (!defenseData.HasData)
            {
                Logger.LogInformation(LogMessage.NoClearRecords, "Shiyu Defense", context.UserId, gameUid);
                return CommandResult.Success(
                    [new CommandText(string.Format(ResponseMessage.NoClearRecords, "Shiyu Defense"))],
                    isEphemeral: true);
            }

            FloorDetail[] nonNull =
                [.. defenseData.AllFloorDetail.Where(x => x is { Node1: not null, Node2: not null })];
            if (nonNull.Length == 0)
            {
                Logger.LogInformation(LogMessage.NoClearRecords, "Shiyu Defense", context.UserId, gameUid);
                return CommandResult.Success(
                    [new CommandText(string.Format(ResponseMessage.NoClearRecords, "Shiyu Defense"))],
                    isEphemeral: true);
            }

            IEnumerable<Task<bool>> updateImageTask = nonNull.SelectMany(x => x.Node1.Avatars.Concat(x.Node2.Avatars))
                .DistinctBy(x => x!.Id)
                .Select(avatar =>
                    m_ImageUpdaterService.UpdateImageAsync(avatar.ToImageData(), ImageProcessors.AvatarProcessor));
            IEnumerable<Task<bool>> updateBuddyTask = nonNull
                .SelectMany(x => new ZzzBuddy?[] { x.Node1.Buddy, x.Node2.Buddy })
                .Where(x => x is not null)
                .DistinctBy(x => x!.Id)
                .Select(buddy => m_ImageUpdaterService.UpdateImageAsync(buddy!.ToImageData(),
                    new ImageProcessorBuilder().Resize(300, 0).Build()));

            var completed = await Task.WhenAll(updateImageTask.Concat(updateBuddyTask));

            if (completed.Any(x => !x))
            {
                Logger.LogError(LogMessage.ImageUpdateError, "Shiyu Defense", context.UserId, gameUid);
                return CommandResult.Failure(CommandFailureReason.ApiError, ResponseMessage.ImageUpdateError);
            }

            var cardContext = new BaseCardGenerationContext<ZzzDefenseData>(context.UserId, defenseData, profile);
            cardContext.SetParameter("server", server);

            Stream card = await m_CardService.GetCardAsync(cardContext);

            return CommandResult.Success([
                    new CommandText($"<@{context.UserId}>'s Shiyu Defense Summary", CommandText.TextType.Header3),
                    new CommandText(
                        $"Cycle start: <t:{defenseData.BeginTime}:f>\nCycle end: <t:{defenseData.EndTime}:f>"),
                    new CommandAttachment("shiyu_card.jpg", card),
                    new CommandText(ResponseMessage.ApiLimitationFooter, CommandText.TextType.Footer)
                ],
                true);
        }
        catch (CommandException e)
        {
            Logger.LogError(e, LogMessage.UnknownError, "Defense", context.UserId, e.Message);
            return CommandResult.Failure(CommandFailureReason.BotError,
                string.Format(ResponseMessage.CardGenError, "Shiyu Defense"));
        }
        catch (Exception e)
        {
            Logger.LogError(e, LogMessage.UnknownError, "Defense", context.UserId, e.Message);
            return CommandResult.Failure(CommandFailureReason.Unknown, ResponseMessage.UnknownError);
        }
    }
}
