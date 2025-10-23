using Mehrak.Application.Builders;
using Mehrak.Application.Services.Common;
using Mehrak.Application.Services.Genshin.Types;
using Mehrak.Application.Utility;
using Mehrak.Domain.Common;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Genshin.Types;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Mehrak.Application.Services.Genshin.Stygian;

public class GenshinStygianApplicationService : BaseApplicationService<GenshinStygianApplicationContext>
{
    private readonly IImageUpdaterService m_ImageUpdaterService;
    private readonly ICardService<StygianData> m_CardService;
    private readonly IApiService<GenshinStygianInformation, BaseHoYoApiContext> m_ApiService;

    public GenshinStygianApplicationService(
        IImageUpdaterService imageUpdaterService,
        ICardService<StygianData> cardService,
        IApiService<GenshinStygianInformation, BaseHoYoApiContext> apiService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        ILogger<GenshinStygianApplicationService> logger)
        : base(gameRoleApi, logger)
    {
        m_ImageUpdaterService = imageUpdaterService;
        m_CardService = cardService;
        m_ApiService = apiService;
    }

    public override async Task<CommandResult> ExecuteAsync(GenshinStygianApplicationContext context)
    {
        try
        {
            var region = context.Server.ToRegion();

            var profile = await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.Genshin, region);

            if (profile == null)
            {
                Logger.LogWarning(LogMessage.InvalidLogin, context.UserId);
                return CommandResult.Failure(CommandFailureReason.AuthError, ResponseMessage.AuthError);
            }

            var gameUid = profile.GameUid;

            var stygianInfo = await m_ApiService.GetAsync(new(context.UserId, context.LtUid, context.LToken, gameUid, region));
            if (!stygianInfo.IsSuccess)
            {
                Logger.LogError(LogMessage.ApiError, "Stygian", context.UserId, gameUid, stygianInfo.ErrorMessage);
                return CommandResult.Failure(CommandFailureReason.ApiError, string.Format(ResponseMessage.ApiError, "Stygian Onslaught data"));
            }

            if (!stygianInfo.Data.IsUnlock)
            {
                Logger.LogInformation("Stygian Onslaught is not unlocked for User {UserId} UID {GameUid}", context.UserId, gameUid);
                return CommandResult.Success([new CommandText("Stygian Onslaught is not unlocked")], isEphemeral: true);
            }

            if (!stygianInfo.Data.Data![0].Single.HasData)
            {
                Logger.LogInformation(LogMessage.NoClearRecords, "Stygian", context.UserId, gameUid);
                return CommandResult.Success([new CommandText(string.Format(ResponseMessage.NoClearRecords, "Stygian Onslaught"))], isEphemeral: true);
            }

            var stygianData = stygianInfo.Data.Data[0].Single;

            var avatarTasks = stygianData.Challenge!.SelectMany(x => x.Teams).Select(x =>
                m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(), ImageProcessors.AvatarProcessor));
            var sideAvatarTasks = stygianData.Challenge!.SelectMany(x => x.BestAvatar).Select(x =>
                m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(),
                    new ImageProcessorBuilder().Resize(0, 150).Build()));
            var monsterImageTask = stygianData.Challenge!.Select(x => x.Monster)
                .Select(x => m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(), new ImageProcessorBuilder().Build()));

            var completed = await Task.WhenAll(avatarTasks.Concat(sideAvatarTasks).Concat(monsterImageTask));

            if (completed.Any(x => !x))
            {
                Logger.LogError(LogMessage.ImageUpdateError, "Stygian", context.UserId,
                    JsonSerializer.Serialize(stygianData));
                return CommandResult.Failure(CommandFailureReason.ApiError, ResponseMessage.ImageUpdateError);
            }

            var card = await m_CardService.GetCardAsync(new BaseCardGenerationContext<StygianData>(context.UserId,
                stygianInfo.Data.Data[0], context.Server, profile));

            return CommandResult.Success([
                 new CommandText($"<@{context.UserId}>'s Stygian Onslaught Summary", CommandText.TextType.Header3),
                 new CommandText($"Cycle start: <t:{stygianInfo.Data.Data[0].Schedule!.StartTime}:f>\n" +
                    $"Cycle end: <t:{stygianInfo.Data.Data[0].Schedule!.EndTime}:f>"),
                 new CommandAttachment("stygian_card.jpg", card),
                 new CommandText(ResponseMessage.ApiLimitationFooter, CommandText.TextType.Footer)],
                 true);
        }
        catch (CommandException e)
        {
            Logger.LogError(e, LogMessage.UnknownError, "Stygian", context.UserId, e.Message);
            return CommandResult.Failure(CommandFailureReason.BotError, string.Format(ResponseMessage.CardGenError, "Stygian Onslaught"));
        }
        catch (Exception e)
        {
            Logger.LogError(e, LogMessage.UnknownError, "Stygian", context.UserId, e.Message);
            return CommandResult.Failure(CommandFailureReason.Unknown, ResponseMessage.UnknownError);
        }
    }
}
