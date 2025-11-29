#region

using Mehrak.Application.Services.Common;
using Mehrak.Application.Utility;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.Domain.Utility;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Genshin.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.Application.Services.Genshin.RealTimeNotes;

internal class GenshinRealTimeNotesApplicationService : BaseApplicationService<GenshinRealTimeNotesApplicationContext>
{
    private readonly IImageRepository m_ImageRepository;
    private readonly IApiService<GenshinRealTimeNotesData, BaseHoYoApiContext> m_ApiService;

    public GenshinRealTimeNotesApplicationService(
        IImageRepository imageRepository,
        IApiService<GenshinRealTimeNotesData, BaseHoYoApiContext> apiService,
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        IUserRepository userRepository,
        ILogger<GenshinRealTimeNotesApplicationService> logger)
        : base(gameRoleApi, userRepository, logger)
    {
        m_ImageRepository = imageRepository;
        m_ApiService = apiService;
    }

    public override async Task<CommandResult> ExecuteAsync(GenshinRealTimeNotesApplicationContext context)
    {
        try
        {
            var server = Enum.Parse<Server>(context.GetParameter<string>("server")!);
            string region = server.ToRegion();

            var profile =
                await GetGameProfileAsync(context.UserId, context.LtUid, context.LToken, Game.Genshin, region);

            if (profile == null)
            {
                Logger.LogWarning(LogMessage.InvalidLogin, context.UserId);
                return CommandResult.Failure(CommandFailureReason.AuthError, ResponseMessage.AuthError);
            }

            await UpdateGameUidAsync(context.UserId, context.LtUid, Game.Genshin, profile.GameUid, server);

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
        Task<Stream> resinImage = m_ImageRepository.DownloadFileToStreamAsync("genshin_resin");
        Task<Stream> expeditionImage = m_ImageRepository.DownloadFileToStreamAsync("genshin_expedition");
        Task<Stream> teapotImage = m_ImageRepository.DownloadFileToStreamAsync("genshin_teapot");
        Task<Stream> weeklyImage = m_ImageRepository.DownloadFileToStreamAsync("genshin_weekly");
        Task<Stream> transformerImage = m_ImageRepository.DownloadFileToStreamAsync("genshin_transformer");

        long currTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long weeklyReset = server.GetNextWeeklyResetUnix();

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
                new CommandAttachment("genshin_resin.png", await resinImage)
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
                new CommandAttachment("genshin_expedition.png", await expeditionImage)
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
                new CommandAttachment("genshin_teapot.png", await teapotImage)
            ),
            new CommandSection([
                    new CommandText("Weekly Bosses", CommandText.TextType.Header3),
                    new CommandText(
                        $"Remaining Resin Discount: {data.RemainResinDiscountNum}/{data.ResinDiscountNumLimit}"),
                    new CommandText($"Resets <t:{weeklyReset}:R>", CommandText.TextType.Footer)
                ],
                new CommandAttachment("genshin_weekly.png", await weeklyImage)
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
                    new CommandAttachment("genshin_transformer.png", await transformerImage)
                ));

        return CommandResult.Success(components, true, true);
    }
}
