using Mehrak.Application.Services.Common;
using Mehrak.Domain.Common;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.Domain.Utility;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Hsr.Types;
using Microsoft.Extensions.Logging;

namespace Mehrak.Application.Services.Hsr.RealTimeNotes;

internal class HsrRealTimeNotesApplicationService : BaseApplicationService<HsrRealTimeNotesApplicationContext>
{
    private readonly IImageRepository m_ImageRepository;
    private readonly IApiService<HsrRealTimeNotesData, BaseHoYoApiContext> m_ApiService;

    public HsrRealTimeNotesApplicationService(
        IImageRepository imageRepository,
        IApiService<HsrRealTimeNotesData, BaseHoYoApiContext> apiService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        ILogger<HsrRealTimeNotesApplicationService> logger)
        : base(gameRoleApi, logger)
    {
        m_ImageRepository = imageRepository;
        m_ApiService = apiService;
    }

    public override async Task<CommandResult> ExecuteAsync(HsrRealTimeNotesApplicationContext context)
    {
        try
        {
            string region = context.Server.ToRegion();

            var profile = await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.HonkaiStarRail, region);

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

            HsrRealTimeNotesData notesData = notesResult.Data;
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

    private async Task<CommandResult> BuildRealTimeNotes(HsrRealTimeNotesData data,
        Server server, string uid)
    {
        var tbpImage = m_ImageRepository.DownloadFileToStreamAsync("hsr_tbp");
        var assignmentImage = m_ImageRepository.DownloadFileToStreamAsync("hsr_assignment");
        var weeklyImage = m_ImageRepository.DownloadFileToStreamAsync("hsr_weekly");
        var rogueImage = m_ImageRepository.DownloadFileToStreamAsync("hsr_rogue");

        var weeklyReset = server.GetNextWeeklyResetUnix();

        List<ICommandResultComponent> components =
        [
            new CommandText($"Honkai Star Rail Real-Time Notes (UID: {uid})", CommandText.TextType.Header2),
            new CommandSection([
                new("Trailblaze Power", CommandText.TextType.Header3),
                new($"{data.CurrentStamina}/{data.MaxStamina}"),
                new(data.CurrentStamina == data.MaxStamina
                            ? "-# Already Full!"
                            : $"-# Recovers <t:{data.StaminaFullTs}:R>")],
                new("hsr_tbp.png", await tbpImage)
            ),
            new CommandSection([
                new("Assignments", CommandText.TextType.Header3),
                new($"{data.AcceptedExpeditionNum}/{data.MaxStamina}"),
                new(data.AcceptedExpeditionNum > 0
                           ? $"{data.AcceptedExpeditionNum}/{data.MaxStamina}"
                           : "None Accepted!", CommandText.TextType.Footer)],
                new("hsr_assignment.png", await assignmentImage)
            ),
            new CommandSection([
                new("Echoes of War", CommandText.TextType.Header3),
                new(data.WeeklyCocoonCnt > 0
                            ? $"Claimed {data.WeeklyCocoonLimit - data.WeeklyCocoonCnt}/{data.WeeklyCocoonLimit}"
                            : "Fully Claimed!"),
                new("Resets <t:{weeklyReset}:R>", CommandText.TextType.Footer)],
                new("hsr_weekly.png", await weeklyImage)
            ),
            new CommandSection([
                new("Simulated Universe", CommandText.TextType.Header3),
                new($"{data.CurrentRogueScore}/{data.MaxRogueScore}"),
                new($"-# Resets <t:{weeklyReset}:R>", CommandText.TextType.Footer)],
                new("hsr_rogue.png", await rogueImage)
            )
        ];

        return CommandResult.Success(components);
    }
}
