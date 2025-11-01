#region

using Mehrak.Application.Services.Common;
using Mehrak.Application.Utility;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.Domain.Utility;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Hsr.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.Application.Services.Hsr.RealTimeNotes;

internal class HsrRealTimeNotesApplicationService : BaseApplicationService<HsrRealTimeNotesApplicationContext>
{
    private readonly IImageRepository m_ImageRepository;
    private readonly IApiService<HsrRealTimeNotesData, BaseHoYoApiContext> m_ApiService;

    public HsrRealTimeNotesApplicationService(
        IImageRepository imageRepository,
        IApiService<HsrRealTimeNotesData, BaseHoYoApiContext> apiService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        IUserRepository userRepository,
        ILogger<HsrRealTimeNotesApplicationService> logger)
        : base(gameRoleApi, userRepository, logger)
    {
        m_ImageRepository = imageRepository;
        m_ApiService = apiService;
    }

    public override async Task<CommandResult> ExecuteAsync(HsrRealTimeNotesApplicationContext context)
    {
        try
        {
            string region = context.Server.ToRegion();

            var profile = await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.HonkaiStarRail,
                region);

            if (profile == null)
            {
                Logger.LogWarning(LogMessage.InvalidLogin, context.UserId);
                return CommandResult.Failure(CommandFailureReason.AuthError, ResponseMessage.AuthError);
            }

            await UpdateGameUidAsync(context.UserId, context.LtUid, Game.HonkaiStarRail, profile.GameUid, context.Server);

            var gameUid = profile.GameUid;

            var notesResult = await m_ApiService.GetAsync(
                new BaseHoYoApiContext(context.UserId, context.LtUid, context.LToken, gameUid, region));

            if (!notesResult.IsSuccess)
            {
                Logger.LogError(LogMessage.ApiError, "Notes", context.UserId, gameUid, notesResult);
                return CommandResult.Failure(CommandFailureReason.ApiError,
                    string.Format(ResponseMessage.ApiError, "Real-Time Notes"));
            }

            HsrRealTimeNotesData notesData = notesResult.Data;

            return await BuildRealTimeNotes(notesData, context.Server, gameUid);
        }
        catch (Exception e)
        {
            Logger.LogError(e, LogMessage.UnknownError, "Notes", context.UserId, e.Message);
            return CommandResult.Failure(CommandFailureReason.Unknown, ResponseMessage.UnknownError);
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
                    new CommandText("Trailblaze Power", CommandText.TextType.Header3),
                    new CommandText($"{data.CurrentStamina}/{data.MaxStamina}"),
                    new CommandText(data.CurrentStamina == data.MaxStamina
                        ? "Already Full!"
                        : $"Recovers <t:{data.StaminaFullTs}:R>", CommandText.TextType.Footer)
                ],
                new CommandAttachment("hsr_tbp.png", await tbpImage)
            ),
            new CommandSection([
                    new CommandText("Assignments", CommandText.TextType.Header3),
                    new CommandText($"{data.AcceptedExpeditionNum}/{data.TotalExpeditionNum}"),
                    new CommandText(data.AcceptedExpeditionNum > 0
                        ? $"{data.AcceptedExpeditionNum}/{data.TotalExpeditionNum}"
                        : "None Accepted!", CommandText.TextType.Footer)
                ],
                new CommandAttachment("hsr_assignment.png", await assignmentImage)
            ),
            new CommandSection([
                    new CommandText("Echoes of War", CommandText.TextType.Header3),
                    new CommandText(data.WeeklyCocoonCnt > 0
                        ? $"Claimed {data.WeeklyCocoonLimit - data.WeeklyCocoonCnt}/{data.WeeklyCocoonLimit}"
                        : "Fully Claimed!"),
                    new CommandText($"Resets <t:{weeklyReset}:R>", CommandText.TextType.Footer)
                ],
                new CommandAttachment("hsr_weekly.png", await weeklyImage)
            ),
            new CommandSection([
                    new CommandText("Simulated Universe", CommandText.TextType.Header3),
                    new CommandText($"{data.CurrentRogueScore}/{data.MaxRogueScore}"),
                    new CommandText($"Resets <t:{weeklyReset}:R>", CommandText.TextType.Footer)
                ],
                new CommandAttachment("hsr_rogue.png", await rogueImage)
            )
        ];

        return CommandResult.Success(components, true, true);
    }
}
