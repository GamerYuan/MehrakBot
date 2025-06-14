#region

using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Models;
using MehrakCore.Modules;
using MehrakCore.Repositories;
using MehrakCore.Services.Commands.Executor;
using MehrakCore.Services.Common;
using MehrakCore.Services.Metrics;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;

#endregion

namespace MehrakCore.Services.Commands.Genshin.RealTimeNotes;

public class GenshinRealTimeNotesCommandExecutor : BaseCommandExecutor<GenshinRealTimeNotesData>,
    IRealTimeNotesCommandExecutor<GenshinCommandModule>
{
    private readonly IRealTimeNotesApiService<GenshinRealTimeNotesData> m_ApiService;
    private readonly ImageRepository m_ImageRepository;

    private Regions m_PendingServer;

    public GenshinRealTimeNotesCommandExecutor(IRealTimeNotesApiService<GenshinRealTimeNotesData> apiService,
        ImageRepository imageRepository, GameRecordApiService gameRecordApi,
        UserRepository userRepository, TokenCacheService tokenCacheService,
        IAuthenticationMiddlewareService authenticationMiddleware, ILogger<GenshinRealTimeNotesData> logger) : base(
        userRepository, tokenCacheService, authenticationMiddleware, gameRecordApi, logger)
    {
        m_ApiService = apiService;
        m_ImageRepository = imageRepository;
    }

    public override async ValueTask ExecuteAsync(params object?[] parameters)
    {
        if (parameters.Length != 2)
            throw new ArgumentException("Invalid parameters count for real-time notes command");

        var server = (Regions?)parameters[0];
        var profile = parameters[1] == null ? 1 : (uint)parameters[1]!;

        try
        {
            var (user, selectedProfile) = await ValidateUserAndProfileAsync(profile);
            if (user == null || selectedProfile == null)
                return;

            // Auto-select server from cache if not provided
            if (selectedProfile.LastUsedRegions != null && !server.HasValue &&
                selectedProfile.LastUsedRegions.TryGetValue(GameName.Genshin, out var tmp))
                server = tmp;

            var cachedServer = server ?? GetCachedServer(selectedProfile, GameName.Genshin);
            if (!await ValidateServerAsync(cachedServer))
                return;

            m_PendingServer = cachedServer!.Value;
            var ltoken = await GetOrRequestAuthenticationAsync(selectedProfile, profile);

            if (ltoken != null)
            {
                Logger.LogInformation("User {UserId} is already authenticated", Context.Interaction.User.Id);
                await Context.Interaction.SendResponseAsync(
                    InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
                await SendRealTimeNotesAsync(selectedProfile.LtUid, ltoken, cachedServer.Value);
            }
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error executing real-time notes command for user {UserId}",
                Context.Interaction.User.Id);
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                new InteractionMessageProperties().WithContent("An error occurred while processing your request")
                    .WithFlags(MessageFlags.Ephemeral)));
        }
    }

    public override async Task OnAuthenticationCompletedAsync(AuthenticationResult result)
    {
        if (result.IsSuccess)
        {
            Context = result.Context;
            Logger.LogInformation("Authentication completed successfully for user {UserId}",
                Context.Interaction.User.Id);
            await SendRealTimeNotesAsync(result.LtUid, result.LToken, m_PendingServer);
        }
        else
        {
            Logger.LogWarning("Authentication failed for user {UserId}: {ErrorMessage}",
                Context.Interaction.User.Id, result.ErrorMessage);
            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .AddComponents(new TextDisplayProperties($"Authentication failed: {result.ErrorMessage}"))
                .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2));
        }
    }

    private async ValueTask SendRealTimeNotesAsync(ulong ltuid, string ltoken, Regions server)
    {
        try
        {
            var region = server.GetRegion();
            var user = await UserRepository.GetUserAsync(Context.Interaction.User.Id);

            var result = await GetAndUpdateGameUidAsync(user, GameName.Genshin, ltuid, ltoken, server,
                server.GetRegion());

            if (!result.IsSuccess) return;

            var gameUid = result.Data;
            var notesResult = await m_ApiService.GetRealTimeNotesAsync(gameUid, region, ltuid, ltoken);

            if (!notesResult.IsSuccess)
            {
                Logger.LogError("Failed to fetch real-time notes: {ErrorMessage}", notesResult.ErrorMessage);
                await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                    new InteractionMessageProperties().WithContent(notesResult.ErrorMessage)
                        .WithFlags(MessageFlags.Ephemeral)));
                return;
            }

            var notesData = notesResult.Data;
            if (notesData == null)
            {
                Logger.LogError("No data found in real-time notes response");
                await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                    new InteractionMessageProperties().WithContent("No data found in real-time notes response")
                        .WithFlags(MessageFlags.Ephemeral)));
                return;
            }

            await Context.Interaction.SendFollowupMessageAsync(await BuildRealTimeNotes(notesData, server, gameUid));
            Logger.LogInformation("Successfully fetched real-time notes for user {UserId} in region {Region}",
                Context.Interaction.User.Id, region);
            BotMetrics.TrackCommand(Context.Interaction.User, "genshin notes", true);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error fetching real-time notes for user {UserId} in region {Region}",
                Context.Interaction.User.Id, server);
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                new InteractionMessageProperties().WithContent("An unknown error occurred, please try again later")
                    .WithFlags(MessageFlags.Ephemeral)));
            BotMetrics.TrackCommand(Context.Interaction.User, "genshin notes", false);
        }
    }

    private async ValueTask<InteractionMessageProperties> BuildRealTimeNotes(GenshinRealTimeNotesData data,
        Regions region, string uid)
    {
        var resinImage = m_ImageRepository.DownloadFileToStreamAsync("genshin_resin");
        var expeditionImage = m_ImageRepository.DownloadFileToStreamAsync("genshin_expedition");
        var teapotImage = m_ImageRepository.DownloadFileToStreamAsync("genshin_teapot");
        var weeklyImage = m_ImageRepository.DownloadFileToStreamAsync("genshin_weekly");
        var transformerImage = m_ImageRepository.DownloadFileToStreamAsync("genshin_transformer");

        var currTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var weeklyReset = region.GetNextWeeklyResetUnix();

        InteractionMessageProperties response = new();
        response.WithFlags(MessageFlags.IsComponentsV2 | MessageFlags.Ephemeral);
        ComponentContainerProperties container =
        [
            new TextDisplayProperties($"## Genshin Impact Real-Time Notes (UID: {uid})"),
            new ComponentSectionProperties(
                    new ComponentSectionThumbnailProperties(
                        new ComponentMediaProperties("attachment://genshin_resin.png")))
                .WithComponents([
                    new TextDisplayProperties("### Original Resin"),
                    new TextDisplayProperties($"{data.CurrentResin}/{data.MaxResin}"),
                    new TextDisplayProperties(data.CurrentResin == data.MaxResin
                        ? "Already Full!"
                        : $"-# Recovers <t:{currTime + long.Parse(data.ResinRecoveryTime!)}:R>")
                ]),
            new ComponentSectionProperties(
                    new ComponentSectionThumbnailProperties(
                        new ComponentMediaProperties("attachment://genshin_expedition.png")))
                .WithComponents([
                    new TextDisplayProperties("### Expeditions"),
                    new TextDisplayProperties(data.CurrentExpeditionNum > 0
                        ? $"{data.CurrentExpeditionNum}/{data.MaxExpeditionNum}"
                        : "None Dispatched!"),
                    new TextDisplayProperties(data.CurrentExpeditionNum > 0
                        ? $"-# Completes <t:{currTime + data.Expeditions!.Max(x => long.Parse(x.RemainedTime!))}:R>"
                        : "-# To be dispatched")
                ]),
            new ComponentSectionProperties(
                    new ComponentSectionThumbnailProperties(
                        new ComponentMediaProperties("attachment://genshin_teapot.png")))
                .WithComponents([
                    new TextDisplayProperties("### Serenitea Pot"),
                    new TextDisplayProperties(data.CurrentHomeCoin == data.MaxHomeCoin
                        ? "Already Full!"
                        : $"{data.CurrentHomeCoin}/{data.MaxHomeCoin}"),
                    new TextDisplayProperties(data.CurrentHomeCoin == data.MaxHomeCoin
                        ? "-# To be collected"
                        : $"-# Recovers <t:{currTime + long.Parse(data.HomeCoinRecoveryTime!)}:R>")
                ]),
            new ComponentSectionProperties(
                    new ComponentSectionThumbnailProperties(
                        new ComponentMediaProperties("attachment://genshin_weekly.png")))
                .WithComponents([
                    new TextDisplayProperties("### Weekly Bosses"),
                    new TextDisplayProperties(
                        $"Remaining Resin Discount: {data.RemainResinDiscountNum}/{data.ResinDiscountNumLimit}"),
                    new TextDisplayProperties(
                        $"-# Resets <t:{weeklyReset}:R>")
                ])
        ];

        response.AddComponents([container]);

        response.WithAttachments([
            new AttachmentProperties("genshin_resin.png", await resinImage),
            new AttachmentProperties("genshin_expedition.png", await expeditionImage),
            new AttachmentProperties("genshin_teapot.png", await teapotImage),
            new AttachmentProperties("genshin_weekly.png", await weeklyImage)
        ]);

        if (data.Transformer?.Obtained == true)
        {
            container.AddComponents([
                new ComponentSectionProperties(
                        new ComponentSectionThumbnailProperties(
                            new ComponentMediaProperties("attachment://genshin_transformer.png")))
                    .WithComponents([
                        new TextDisplayProperties("### Parametric Transformer"),
                        new TextDisplayProperties(data.Transformer.RecoveryTime!.Reached ? "Not Claimed!" : "Claimed!"),
                        new TextDisplayProperties(
                            $"-# Resets <t:{weeklyReset}:R>")
                    ])
            ]);
            response.AddAttachments(new AttachmentProperties("genshin_transformer.png", await transformerImage));
        }

        return response;
    }
}