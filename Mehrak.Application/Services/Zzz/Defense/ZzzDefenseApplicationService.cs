using Mehrak.Application.Builders;
using Mehrak.Application.Services.Common;
using Mehrak.Application.Services.Genshin.Types;
using Mehrak.Application.Utility;
using Mehrak.Domain.Common;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Zzz.Types;
using Microsoft.Extensions.Logging;

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
        ILogger<ZzzDefenseApplicationService> logger)
        : base(gameRoleApi, logger)
    {
        m_CardService = cardService;
        m_ImageUpdaterService = imageUpdaterService;
        m_ApiService = apiService;
    }

    public override async Task<CommandResult> ExecuteAsync(ZzzDefenseApplicationContext context)
    {
        try
        {
            string region = context.Server.ToRegion();

            var profile = await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.ZenlessZoneZero, region);

            if (profile == null)
            {
                Logger.LogWarning("No profile found for user {UserId}", context.UserId);
                return CommandResult.Failure("Invalid HoYoLAB UID or Cookies. Please authenticate again");
            }

            var gameUid = profile.GameUid;

            var defenseResponse = await m_ApiService.GetAsync(new(context.UserId, context.LtUid, context.LToken, gameUid, region));

            if (!defenseResponse.IsSuccess)
            {
                Logger.LogWarning("Failed to fetch Zzz Assault data for user {UserId}: {Error}",
                    context.UserId, defenseResponse.ErrorMessage);
                return CommandResult.Failure(defenseResponse.ErrorMessage);
            }

            var defenseData = defenseResponse.Data!;

            if (!defenseData.HasData)
            {
                Logger.LogInformation("No Shiyu Defense clear records found for user {UserId}",
                    context.UserId);
                return CommandResult.Failure("No Shiyu Defense clear records found");
            }

            FloorDetail[] nonNull = [.. defenseData.AllFloorDetail.Where(x => x is { Node1: not null, Node2: not null })];
            if (nonNull.Length == 0)
            {
                Logger.LogInformation("No Shiyu Defense clear records found for user {UserId}",
                    context.UserId);
                return CommandResult.Failure("No Shiyu Defense clear records found");
            }

            IEnumerable<Task> updateImageTask = nonNull.SelectMany(x => x.Node1.Avatars.Concat(x.Node2.Avatars))
                .DistinctBy(x => x!.Id)
                .Select(avatar => m_ImageUpdaterService.UpdateImageAsync(avatar.ToImageData(), ImageProcessors.AvatarProcessor));
            IEnumerable<Task> updateBuddyTask = nonNull.SelectMany(x => new ZzzBuddy?[] { x.Node1.Buddy, x.Node2.Buddy })
                .Where(x => x is not null)
                .DistinctBy(x => x!.Id)
                .Select(buddy => m_ImageUpdaterService.UpdateImageAsync(buddy!.ToImageData(),
                    new ImageProcessorBuilder().Resize(300, 0).Build()));

            await Task.WhenAll(updateImageTask.Concat(updateBuddyTask));

            var card = await m_CardService.GetCardAsync(
                new BaseCardGenerationContext<ZzzDefenseData>(context.UserId, defenseData, context.Server, profile));

            return CommandResult.Success([
                new CommandText($"<@{context.UserId}>'s Shiyu Defense Summary", CommandText.TextType.Header3),
                new CommandText($"Cycle start: <t:{defenseData.BeginTime}:f>\nCycle end: <t:{defenseData.EndTime}:f>"),
                new CommandAttachment("shiyu_card.jpg", card),
                new CommandText($"Information may be inaccurate due to API limitations. Please check in-game for the most accurate data.",
                    CommandText.TextType.Footer)],
                true);
        }
        catch (CommandException e)
        {
            Logger.LogError(e, "Error fetching Zzz Defense data for user {UserId}", context.UserId);
            return CommandResult.Failure(e.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unexpected error fetching Zzz Defense data for user {UserId}", context.UserId);
            return CommandResult.Failure("An unknown error occurred while processing your request");
        }
    }
}
