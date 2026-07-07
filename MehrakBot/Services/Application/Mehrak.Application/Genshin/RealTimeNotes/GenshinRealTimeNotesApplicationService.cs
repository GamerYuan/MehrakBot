#region

using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Services;
using Mehrak.Application.Shared.Utility;
using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.Domain.Shared.Utility;
using Mehrak.Domain.User.Models;
using Mehrak.GameApi.GameRole;
using Mehrak.GameApi.Genshin.Types;
using Mehrak.GameApi.Shared.Types;
using Mehrak.Infrastructure.User;

#endregion

namespace Mehrak.Application.Genshin.RealTimeNotes;

internal class GenshinRealTimeNotesApplicationService : BaseApplicationService
{
    private readonly IApiService<GenshinRealTimeNotesData, BaseHoYoApiContext> m_ApiService;

    protected override string CommandName => "Genshin Notes";

    public GenshinRealTimeNotesApplicationService(
        IApiService<GenshinRealTimeNotesData, BaseHoYoApiContext> apiService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        UserDbContext userContext,
        ILogger<GenshinRealTimeNotesApplicationService> logger)
        : base(gameRoleApi, userContext, logger)
    {
        m_ApiService = apiService;
    }

    protected override async Task<CommandResult> ExecuteCommandAsync(IApplicationContext context, CancellationToken cancellationToken = default)
    {
        var server = Enum.Parse<Server>(context.GetParameter("server")!);
        var region = server.ToRegion();

        var (profile, notesResult) = await FetchProfileAndPrimaryAsync(
            context.UserId, context.LtUid, context.LToken, Game.Genshin, region,
            uid => m_ApiService.GetAsync(
                new BaseHoYoApiContext(context.UserId, context.LtUid, context.LToken, uid, region), cancellationToken),
            cancellationToken);

        if (!notesResult.IsSuccess)
        {
            if (notesResult.StatusCode == StatusCode.Cancelled)
                throw new OperationCanceledException(notesResult.ErrorMessage ?? "Cancelled");
            if (notesResult.StatusCode == StatusCode.Timeout)
                return CommandResult.Failure(CommandFailureReason.Timeout, ResponseMessage.TimeoutError);
            Logger.LogError(LogMessage.ApiError, "Notes", context.UserId, profile.GameUid, notesResult);
            return CommandResult.Failure(CommandFailureReason.ApiError,
                string.Format(ResponseMessage.ApiError, "Real-Time Notes data"));
        }

        return await BuildRealTimeNotes(notesResult.Data, server, profile.GameUid);

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
                new CommandAttachment("genshin_resin.png", AttachmentSourceType.ImageStorage, "genshin/resin.png")
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
                new CommandAttachment("genshin_expedition.png", AttachmentSourceType.ImageStorage, "genshin/expedition.png")
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
                new CommandAttachment("genshin_teapot.png", AttachmentSourceType.ImageStorage, "genshin/teapot.png")
            ),
            new CommandSection([
                    new CommandText("Weekly Bosses", CommandText.TextType.Header3),
                    new CommandText(
                        $"Remaining Resin Discount: {data.RemainResinDiscountNum}/{data.ResinDiscountNumLimit}"),
                    new CommandText($"Resets <t:{weeklyReset}:R>", CommandText.TextType.Footer)
                ],
                new CommandAttachment("genshin_weekly.png", AttachmentSourceType.ImageStorage, "genshin/weekly.png")
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
                    new CommandAttachment("genshin_transformer.png", AttachmentSourceType.ImageStorage, "genshin/transformer.png")
                ));

        return CommandResult.Success(components, true, true);
    }
}
