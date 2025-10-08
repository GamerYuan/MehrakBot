#region

using Mehrak.Domain.Interfaces;
using MehrakCore.ApiResponseTypes.Hsr;
using MehrakCore.Models;
using MehrakCore.Modules;
using MehrakCore.Services.Commands.Hsr.EndGame.PureFiction;
using MehrakCore.Services.Common;
using MehrakCore.Services.Metrics;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;

#endregion

namespace MehrakCore.Services.Commands.Hsr.EndGame.BossChallenge;

public class HsrBossChallengeCommandExecutor : BaseHsrEndGameCommandExecutor
{
    private readonly ImageUpdaterService<HsrCharacterInformation> m_ImageUpdaterService;
    private readonly HsrEndGameApiService m_ApiService;
    private Regions m_PendingServer;

    protected override string GameModeName { get; } = "Apocalyptic Shadow";
    protected override string AttachmentName { get; } = "as_card";

    public HsrBossChallengeCommandExecutor(UserRepository userRepository, TokenCacheService tokenCacheService,
        IAuthenticationMiddlewareService authenticationMiddleware, GameRecordApiService gameRecordApi,
        ILogger<HsrCommandModule> logger, ICommandService<BaseHsrEndGameCommandExecutor> commandService,
        ImageUpdaterService<HsrCharacterInformation> imageUpdaterService,
        IApiService<BaseHsrEndGameCommandExecutor> apiService) : base(
        userRepository, tokenCacheService, authenticationMiddleware, gameRecordApi, logger, commandService)
    {
        m_ImageUpdaterService = imageUpdaterService;
        m_ApiService = (HsrEndGameApiService)apiService;
    }

    public override async ValueTask ExecuteAsync(params object?[] parameters)
    {
        if (parameters.Length != 2)
            throw new ArgumentException("Invalid number of parameters provided.");

        var server = (Regions?)parameters[0];
        var profile = (uint)(parameters[1] ?? 1);

        try
        {
            var (user, selectedProfile) = await ValidateUserAndProfileAsync(profile);
            if (user == null || selectedProfile == null) return;

            server ??= GetCachedServer(selectedProfile, GameName.HonkaiStarRail);
            if (server == null)
            {
                await SendErrorMessageAsync("No cached server found! Please select a server first.", false);
                return;
            }

            m_PendingServer = server.Value;

            var ltoken = await GetOrRequestAuthenticationAsync(selectedProfile, profile);
            if (ltoken != null)
            {
                await Context.Interaction.SendResponseAsync(
                    InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
                await SendBossChallengeCardAsync(server.Value, selectedProfile.LtUid, ltoken).ConfigureAwait(false);
            }
        }
        catch (CommandException e)
        {
            Logger.LogError(e, "Error processing Apocalyptic Shadow command for user {UserId} profile {Profile}",
                Context.Interaction.User.Id, profile);
            await SendErrorMessageAsync(e.Message, false);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing Apocalyptic Shadow command for user {UserId} profile {Profile}",
                Context.Interaction.User.Id, profile);
            await SendErrorMessageAsync(followup: false);
        }
    }

    public override async Task OnAuthenticationCompletedAsync(AuthenticationResult result)
    {
        if (!result.IsSuccess)
        {
            Logger.LogWarning("Authentication failed for user {UserId}: {ErrorMessage}", Context.Interaction.User.Id,
                result.ErrorMessage);
            return;
        }

        Context = result.Context;
        Logger.LogInformation("Authentication completed successfully for user {UserId}",
            Context.Interaction.User.Id);
        await SendBossChallengeCardAsync(m_PendingServer, result.LtUid, result.LToken).ConfigureAwait(false);
    }

    private async ValueTask SendBossChallengeCardAsync(Regions server, ulong ltuid, string ltoken)
    {
        try
        {
            var region = server.GetRegion();
            var user = await UserRepository.GetUserAsync(Context.Interaction.User.Id);
            var response =
                await GetAndUpdateGameDataAsync(user, GameName.HonkaiStarRail, ltuid, ltoken, server, region);
            if (!response.IsSuccess)
                return;

            var gameData = response.Data;
            var challengeResponse =
                await m_ApiService.GetEndGameDataAsync(gameData.GameUid!, region, ltuid, ltoken,
                    EndGameMode.ApocalypticShadow);
            if (!challengeResponse.IsSuccess)
            {
                Logger.LogError("Failed to fetch Apocalyptic Shadow data for user {UserId}: {ErrorMessage}",
                    Context.Interaction.User.Id, challengeResponse.ErrorMessage);
                await SendErrorMessageAsync(challengeResponse.ErrorMessage);
                return;
            }

            var challengeData = challengeResponse.Data;
            if (!challengeData.HasData)
            {
                Logger.LogInformation("No Apocalyptic Shadow clear records found for user {UserId}",
                    Context.Interaction.User.Id);
                await SendErrorMessageAsync("No Apocalyptic Shadow clear records found");
                return;
            }

            var nonNull = challengeData.AllFloorDetail.Where(x => x is { Node1: not null, Node2: not null }).ToList();
            if (nonNull.Count == 0)
            {
                Logger.LogInformation("No Apocalyptic Shadow clear records found for user {UserId}",
                    Context.Interaction.User.Id);
                await SendErrorMessageAsync("No Apocalyptic Shadow clear records found");
                return;
            }

            var tasks = nonNull
                .SelectMany(x => x.Node1!.Avatars.Concat(x.Node2!.Avatars))
                .DistinctBy(x => x.Id)
                .Select(async x =>
                    await m_ImageUpdaterService.UpdateAvatarAsync(x.Id.ToString(), x.Icon));
            await Task.WhenAll(tasks);

            var buffMap = await m_ApiService.GetBuffMapAsync(challengeData);

            var message = await GetEndGameCardAsync(EndGameMode.ApocalypticShadow, response.Data, challengeData, server,
                buffMap);
            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                .AddComponents(new TextDisplayProperties("Command execution completed")));
            await Context.Interaction.SendFollowupMessageAsync(message);
            BotMetrics.TrackCommand(Context.Interaction.User, "hsr as", true);
        }
        catch (CommandException e)
        {
            Logger.LogError(e, "Error processing Apocalyptic Shadow card for user {UserId}",
                Context.Interaction.User.Id);
            await SendErrorMessageAsync(e.Message);
            BotMetrics.TrackCommand(Context.Interaction.User, "hsr as", true);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error processing Apocalyptic Shadow card for user {UserId}",
                Context.Interaction.User.Id);
            await SendErrorMessageAsync("An error occurred while generating Apocalyptic Shadow card");
            BotMetrics.TrackCommand(Context.Interaction.User, "hsr as", true);
        }
    }
}
