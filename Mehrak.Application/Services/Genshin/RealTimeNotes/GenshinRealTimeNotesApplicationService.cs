using Mehrak.Application.Services.Common;
using Mehrak.Domain.Common;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.Domain.Utility;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Genshin.Types;
using Microsoft.Extensions.Logging;

namespace Mehrak.Application.Services.Genshin.RealTimeNotes;

internal class GenshinRealTimeNotesApplicationService : BaseApplicationService<GenshinRealTimeNotesApplicationContext>
{
    private readonly IImageRepository m_ImageRepository;
    private readonly IApiService<GenshinRealTimeNotesData, BaseHoYoApiContext> m_ApiService;

    public GenshinRealTimeNotesApplicationService(
        IImageRepository imageRepository,
        IApiService<GenshinRealTimeNotesData, BaseHoYoApiContext> apiService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        ILogger<GenshinRealTimeNotesApplicationService> logger)
        : base(gameRoleApi, logger)
    {
        m_ImageRepository = imageRepository;
        m_ApiService = apiService;
    }

    public override async Task<CommandResult> ExecuteAsync(GenshinRealTimeNotesApplicationContext context)
    {
        try
        {
            string region = context.Server.ToRegion();

            var profile = await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.Genshin, region);

            if (profile == null)
            {
                Logger.LogWarning("No profile found for user {UserId}", context.UserId);
                return CommandResult.Failure("Invalid HoYoLAB UID or Cookies. Please authenticate again");
            }

            var gameUid = profile.GameUid;

            var notesResult = await m_ApiService.GetAsync(
                new(context.UserId, context.LtUid, context.LToken, gameUid, region));

            if (!notesResult.IsSuccess)
            {
                Logger.LogWarning("Failed to fetch Real Time Notes information for gameUid: {GameUid}, region: {Server}, error: {Error}",
                    profile.GameUid, context.Server, notesResult.ErrorMessage);
                return CommandResult.Failure(notesResult.ErrorMessage);
            }

            GenshinRealTimeNotesData notesData = notesResult.Data;
            if (notesData == null)
            {
                Logger.LogWarning("No data found in real-time notes response");
                return CommandResult.Failure("No data found in real-time notes response");
            }

            return await BuildRealTimeNotes(notesData, context.Server, gameUid);
        }
        catch (CommandException e)
        {
            Logger.LogError(e, "Error fetching real-time notes for user {UserId} in region {Region}",
                context.UserId, context.Server);
            return CommandResult.Failure(e.Message);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error fetching real-time notes for user {UserId} in region {Region}",
                context.UserId, context.Server);
            return CommandResult.Failure("An error occurred while retrieving Real Time Notes data");
        }
    }

    private async Task<CommandResult> BuildRealTimeNotes(GenshinRealTimeNotesData data,
        Server server, string uid)
    {
        Task<Stream> resinImage = m_ImageRepository.DownloadFileToStreamAsync("genshin_resin");
        Task<Stream> expeditionImage = m_ImageRepository.DownloadFileToStreamAsync("genshin_expedition");
        Task<Stream> teapotImage = m_ImageRepository.DownloadFileToStreamAsync("genshin_teapot");
        Task<Stream> weeklyImage = m_ImageRepository.DownloadFileToStreamAsync("genshin_weekly");
        Task<Stream> transformerImage = m_ImageRepository.DownloadFileToStreamAsync("genshin_transformer");

        long currTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long weeklyReset = server.GetNextWeeklyResetUnix();

        List<CommandSection> sections = [
            new CommandSection(
                "Original Resin",
                $"{data.CurrentResin}/{data.MaxResin}",
                data.CurrentResin == data.MaxResin
                        ? "Already Full!"
                        : $"Recovers <t:{currTime + long.Parse(data.ResinRecoveryTime!)}:R>",
                new("genshin_resin.png", await resinImage)
                ),
            new CommandSection(
                "Expeditions",
                data.CurrentExpeditionNum > 0
                        ? $"{data.CurrentExpeditionNum}/{data.MaxExpeditionNum}"
                        : "None Dispatched!",
                data.CurrentExpeditionNum > 0
                        ? data.Expeditions!.Max(x => long.Parse(x.RemainedTime!)) > 0
                            ? $"Completes <t:{currTime + data.Expeditions!.Max(x => long.Parse(x.RemainedTime!))}:R>"
                            : "All Expeditions Completed"
                        : "To be dispatched",
                new("genshin_expedition.png", await expeditionImage)
                ),
            new CommandSection(
                    "Serenitea Pot",
                    data.CurrentHomeCoin == data.MaxHomeCoin
                        ? "Already Full!"
                        : $"{data.CurrentHomeCoin}/{data.MaxHomeCoin}",
                    data.CurrentHomeCoin == data.MaxHomeCoin
                        ? "To be collected"
                        : $"Recovers <t:{currTime + long.Parse(data.HomeCoinRecoveryTime!)}:R>",
                    new("genshin_teapot.png", await teapotImage)
                ),
            new CommandSection(
                    "Weekly Bosses",
                    $"Remaining Resin Discount: {data.RemainResinDiscountNum}/{data.ResinDiscountNumLimit}",
                    $"Resets <t:{weeklyReset}:R>",
                    new("genshin_weekly.png", await weeklyImage)
                )

        ];

        if (data.Transformer?.Obtained == true)
        {
            sections.Add(
                new CommandSection(
                    "Parametric Transformer",
                    data.Transformer.RecoveryTime!.Reached
                        ? "Not Claimed!"
                        : "Claimed!",
                    $"Resets <t:{weeklyReset}:R>",
                    new("genshin_transformer.png", await transformerImage)
                ));
        }

        return CommandResult.Success($"Genshin Impact Real-Time Notes (UID: {uid})", sections: sections);
    }
}
