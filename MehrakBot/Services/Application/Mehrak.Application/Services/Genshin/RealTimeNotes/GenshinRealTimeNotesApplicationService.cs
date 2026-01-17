#region

using Mehrak.Application.Services.Abstractions;
using Mehrak.Application.Services.Common;
using Mehrak.Application.Utility;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.Domain.Utility;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Genshin.Types;
using Mehrak.Infrastructure.Context;

#endregion

namespace Mehrak.Application.Services.Genshin.RealTimeNotes;

internal class GenshinRealTimeNotesApplicationService : BaseApplicationService
{
    private readonly IApiService<GenshinRealTimeNotesData, BaseHoYoApiContext> m_ApiService;

    public GenshinRealTimeNotesApplicationService(
        IApiService<GenshinRealTimeNotesData, BaseHoYoApiContext> apiService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        UserDbContext userContext,
        ILogger<GenshinRealTimeNotesApplicationService> logger)
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

            var profile =
                await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.Genshin, region);

            if (profile == null)
            {
                Logger.LogWarning(LogMessage.InvalidLogin, context.UserId);
                return CommandResult.Failure(CommandFailureReason.AuthError, ResponseMessage.AuthError);
            }

            await UpdateGameUidAsync(context.UserId, context.LtUid, Game.Genshin, profile.GameUid, server.ToString());

            var gameUid = profile.GameUid;

            var notesResult = await m_ApiService.GetAsync(
                new BaseHoYoApiContext(context.UserId, context.LtUid, context.LToken, gameUid, region));

            if (!notesResult.IsSuccess)
            {
                Logger.LogError(LogMessage.ApiError, "Notes", context.UserId, gameUid, notesResult);
                return CommandResult.Failure(CommandFailureReason.ApiError,
                    string.Format(ResponseMessage.ApiError, "Real-Time Notes data"));
            }

            return await BuildRealTimeNotes(notesResult.Data, server, gameUid);
        }
        catch (Exception e)
        {
            Logger.LogError(e, LogMessage.UnknownError, "Notes", context.UserId, e.Message);
            return CommandResult.Failure(CommandFailureReason.Unknown, ResponseMessage.UnknownError);
        }
    }

    private async Task<CommandResult> BuildRealTimeNotes(GenshinRealTimeNotesData data,
        Server server, string uid)
    {
        var currTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var weeklyReset = server.GetNextWeeklyResetUnix();

        List<ICommandResultComponent> components =
        [
            new CommandText($"Real Time Notes for UID: {uid}", CommandText.TextType.Header2),
            new CommandSection([
                    new CommandText("Original Resin", CommandText.TextType.Header3),
                    new CommandText($"{data.CurrentResin}/{data.MaxResin}"),
                    new CommandText(data.CurrentResin == data.MaxResin
                            ? "Already Full!"
                            : $"Recovers <t:{currTime + long.Parse(data.ResinRecoveryTime!)}:R>",
                        CommandText.TextType.Footer)
                ],
                new StoredAttachment("genshin_resin.png", AttachmentSourceType.ImageStorage)
            ),
            new CommandSection([
                    new CommandText("Expeditions", CommandText.TextType.Header3),
                    new CommandText(data.CurrentExpeditionNum > 0
                        ? $"{data.CurrentExpeditionNum}/{data.MaxExpeditionNum}"
                        : "None Dispatched!"),
                    new CommandText(data.CurrentExpeditionNum > 0
                        ? data.Expeditions!.Max(x => long.Parse(x.RemainedTime!)) > 0
                            ? $"Completes <t:{currTime + data.Expeditions!.Max(x => long.Parse(x.RemainedTime!))}:R>"
                            : "All Expeditions Completed"
                        : "To be dispatched", CommandText.TextType.Footer)
                ],
                new StoredAttachment("genshin_expedition.png", AttachmentSourceType.ImageStorage)
            ),
            new CommandSection([
                    new CommandText("Serenitea Pot", CommandText.TextType.Header3),
                    new CommandText(data.CurrentHomeCoin == data.MaxHomeCoin
                        ? "Already Full!"
                        : $"{data.CurrentHomeCoin}/{data.MaxHomeCoin}"),
                    new CommandText(data.CurrentHomeCoin == data.MaxHomeCoin
                            ? "To be collected"
                            : $"Recovers <t:{currTime + long.Parse(data.HomeCoinRecoveryTime!)}:R>",
                        CommandText.TextType.Footer)
                ],
                new StoredAttachment("genshin_teapot.png", AttachmentSourceType.ImageStorage)
            ),
            new CommandSection([
                    new CommandText("Weekly Bosses", CommandText.TextType.Header3),
                    new CommandText(
                        $"Remaining Resin Discount: {data.RemainResinDiscountNum}/{data.ResinDiscountNumLimit}"),
                    new CommandText($"Resets <t:{weeklyReset}:R>", CommandText.TextType.Footer)
                ],
                new StoredAttachment("genshin_weekly.png", AttachmentSourceType.ImageStorage)
            )
        ];

        if (data.Transformer?.Obtained == true)
            components.Add(
                new CommandSection([
                        new CommandText("Parametric Transformer", CommandText.TextType.Header3),
                        new CommandText(data.Transformer.RecoveryTime!.Reached
                            ? "Not Claimed!"
                            : "Claimed!"),
                        new CommandText($"Resets <t:{weeklyReset}:R>", CommandText.TextType.Footer)
                    ],
                    new StoredAttachment("genshin_transformer.png", AttachmentSourceType.ImageStorage)
                ));

        return CommandResult.Success(components, true, true);
    }
}
