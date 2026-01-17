#region

using Mehrak.Application.Services.Abstractions;
using Mehrak.Application.Services.Common;
using Mehrak.Application.Utility;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.Domain.Utility;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Hsr.Types;
using Mehrak.Infrastructure.Context;

#endregion

namespace Mehrak.Application.Services.Hsr.RealTimeNotes;

internal class HsrRealTimeNotesApplicationService : BaseApplicationService
{
    private readonly IApiService<HsrRealTimeNotesData, BaseHoYoApiContext> m_ApiService;

    public HsrRealTimeNotesApplicationService(
        IApiService<HsrRealTimeNotesData, BaseHoYoApiContext> apiService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        UserDbContext userContext,
        ILogger<HsrRealTimeNotesApplicationService> logger)
        : base(gameRoleApi, userContext, logger)
    {
        m_ApiService = apiService;
    }

    public override async Task<CommandResult> ExecuteAsync(IApplicationContext context)
    {
        try
        {
            var server = Enum.Parse<Server>(context.GetParameter("server")!);
            var region = server.ToRegion();

            var profile = await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.HonkaiStarRail,
                region);

            if (profile == null)
            {
                Logger.LogWarning(LogMessage.InvalidLogin, context.UserId);
                return CommandResult.Failure(CommandFailureReason.AuthError, ResponseMessage.AuthError);
            }

            await UpdateGameUidAsync(context.UserId, context.LtUid, Game.HonkaiStarRail, profile.GameUid, server.ToString());

            var gameUid = profile.GameUid;

            var notesResult = await m_ApiService.GetAsync(
                new BaseHoYoApiContext(context.UserId, context.LtUid, context.LToken, gameUid, region));

            if (!notesResult.IsSuccess)
            {
                Logger.LogError(LogMessage.ApiError, "Notes", context.UserId, gameUid, notesResult);
                return CommandResult.Failure(CommandFailureReason.ApiError,
                    string.Format(ResponseMessage.ApiError, "Real-Time Notes"));
            }

            var notesData = notesResult.Data;

            return await BuildRealTimeNotes(notesData, server, gameUid);
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
        var weeklyReset = server.GetNextWeeklyResetUnix();
        var nextWeeklyReset = server.GetNextNextWeeklyResetUnix();

        var tzi = server.GetTimeZoneInfo();

        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tzi);
        var firstCwReset = new DateTime(2025, 11, 10, 4, 0, 0, DateTimeKind.Unspecified);

        var isCwReset = (int)((nowLocal - firstCwReset).TotalDays / 7) % 2 == 1;

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
                new StoredAttachment("hsr_tbp.png", AttachmentSourceType.ImageStorage)
            ),
            new CommandSection([
                    new CommandText("Assignments", CommandText.TextType.Header3),
                    new CommandText($"{data.AcceptedExpeditionNum}/{data.TotalExpeditionNum}"),
                    new CommandText(data.AcceptedExpeditionNum > 0
                        ? $"{data.AcceptedExpeditionNum}/{data.TotalExpeditionNum}"
                        : "None Accepted!", CommandText.TextType.Footer)
                ],
                new StoredAttachment("hsr_assignment.png", AttachmentSourceType.ImageStorage)
            ),
            new CommandSection([
                    new CommandText("Echoes of War", CommandText.TextType.Header3),
                    new CommandText(data.WeeklyCocoonCnt > 0
                        ? $"Claimed {data.WeeklyCocoonLimit - data.WeeklyCocoonCnt}/{data.WeeklyCocoonLimit}"
                        : "Fully Claimed!"),
                    new CommandText($"Resets <t:{weeklyReset}:R>", CommandText.TextType.Footer)
                ],
                new StoredAttachment("hsr_weekly.png", AttachmentSourceType.ImageStorage)
            ),
            new CommandSection([
                    new CommandText("Simulated Universe", CommandText.TextType.Header3),
                    new CommandText($"{data.CurrentRogueScore}/{data.MaxRogueScore}"),
                    new CommandText(isCwReset ? $"Resets <t:{nextWeeklyReset}:R>" : $"Resets <t:{weeklyReset}:R>", CommandText.TextType.Footer)
                ],
                new StoredAttachment("hsr_rogue.png", AttachmentSourceType.ImageStorage)
            ),
            new CommandSection([
                    new CommandText("Currency Wars", CommandText.TextType.Header3),
                    new CommandText($"{data.GridFightWeeklyCur}/{data.GridFightWeeklyMax}"),
                    new CommandText(isCwReset ? $"Resets <t:{weeklyReset}:R>" : $"Resets <t:{nextWeeklyReset}:R>", CommandText.TextType.Footer)
                ],
                new StoredAttachment("hsr_gridfight.png", AttachmentSourceType.ImageStorage)
            )
        ];

        return CommandResult.Success(components, true, true);
    }
}
