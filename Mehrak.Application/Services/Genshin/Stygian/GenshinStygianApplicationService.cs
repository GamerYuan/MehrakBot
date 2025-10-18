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
                Logger.LogWarning("No profile found for user {UserId}", context.UserId);
                return CommandResult.Failure("Invalid HoYoLAB UID or Cookies. Please authenticate again");
            }

            var gameUid = profile.GameUid;

            var stygianInfo = await m_ApiService.GetAsync(new(context.UserId, context.LtUid, context.LToken, gameUid, region));
            if (!stygianInfo.IsSuccess)
            {
                Logger.LogWarning("Failed to fetch Stygian information for gameUid: {GameUid}, region: {Server}, error: {Error}",
                    profile.GameUid, context.Server, stygianInfo.ErrorMessage);
                return CommandResult.Failure(stygianInfo.ErrorMessage);
            }

            if (!stygianInfo.Data.IsUnlock)
            {
                Logger.LogWarning("Stygian Onslaught is not unlocked for user {UserId}", context.UserId);
                return CommandResult.Failure("Stygian Onslaught is not unlocked");
            }

            if (!stygianInfo.Data.Data![0].Single.HasData)
            {
                Logger.LogWarning("No Stygian Onslaught data found for this cycle for user {UserId}",
                    context.UserId);
                return CommandResult.Failure("No Stygian Onslaught data found for this cycle");
            }

            var stygianData = stygianInfo.Data.Data[0].Single;

            var avatarTasks = stygianData.Challenge!.SelectMany(x => x.Teams).Select(x =>
                m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(), ImageProcessors.AvatarProcessor));
            var sideAvatarTasks = stygianData.Challenge!.SelectMany(x => x.BestAvatar).Select(x =>
                m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(),
                    new ImageProcessorBuilder().Resize(0, 150).Build()));
            var monsterImageTask = stygianData.Challenge!.Select(x => x.Monster)
                .Select(x => m_ImageUpdaterService.UpdateImageAsync(x.ToImageData(), new ImageProcessorBuilder().Build()));

            await Task.WhenAll(avatarTasks.Concat(sideAvatarTasks).Concat(monsterImageTask));

            var card = await m_CardService.GetCardAsync(new BaseCardGenerationContext<StygianData>(context.UserId,
                stygianInfo.Data.Data[0], context.Server, profile));

            return CommandResult.Success(
                 $"<@{context.UserId}>'s Stygian Onslaught Summary",
                 $"Cycle start: <t:{stygianInfo.Data.Data[0].Schedule!.StartTime}:f>\nCycle end: <t:{stygianInfo.Data.Data[0].Schedule!.EndTime}:f>",
                 $"-# Information may be inaccurate due to API limitations. Please check in-game for the most accurate data.",
                 [new("abyss_card.jpg", card)]);
        }
        catch (CommandException e)
        {
            Logger.LogError(e, "Failed to get Stygian Onslaught card for user {UserId}",
                context.UserId);
            return CommandResult.Failure(e.Message);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Failed to get Stygian Onslaught card for user {UserId}",
                context.UserId);
            return CommandResult.Failure("An error occurred while generating Stygian Onslaught card");
        }
    }
}
