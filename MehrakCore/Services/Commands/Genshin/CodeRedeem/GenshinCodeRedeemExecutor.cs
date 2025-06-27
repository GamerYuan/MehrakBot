#region

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

namespace MehrakCore.Services.Commands.Genshin.CodeRedeem;

public class GenshinCodeRedeemExecutor : BaseCommandExecutor<GenshinCommandModule>,
    ICodeRedeemExecutor<GenshinCommandModule>
{
    private readonly ICodeRedeemApiService<GenshinCommandModule> m_ApiService;
    private string m_PendingCode = string.Empty;
    private Regions? m_PendingServer;

    public GenshinCodeRedeemExecutor(UserRepository userRepository, TokenCacheService tokenCacheService,
        IAuthenticationMiddlewareService authenticationMiddleware, GameRecordApiService gameRecordApi,
        ICodeRedeemApiService<GenshinCommandModule> apiService, ILogger<GenshinCommandModule> logger) : base(
        userRepository,
        tokenCacheService, authenticationMiddleware,
        gameRecordApi, logger)
    {
        m_ApiService = apiService;
    }

    public override async ValueTask ExecuteAsync(params object?[] parameters)
    {
        if (parameters.Length != 3) throw new ArgumentException("Invalid number of parameters provided.");

        var code = (string)parameters[0]!;
        var server = (Regions?)parameters[1];
        var profile = parameters[2] == null ? 1 : (uint)parameters[2]!;

        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Code cannot be null or empty.");

        code = code.ToUpperInvariant().Trim();

        try
        {
            Logger.LogInformation("User {UserId} used the code command", Context.Interaction.User.Id);

            var (user, selectedProfile) = await ValidateUserAndProfileAsync(profile);
            if (user == null || selectedProfile == null)
                return;

            var cachedServer = server ?? GetCachedServer(selectedProfile, GameName.Genshin);
            if (!await ValidateServerAsync(cachedServer))
                return;

            m_PendingCode = code;
            m_PendingServer = cachedServer!.Value;

            var ltoken = await GetOrRequestAuthenticationAsync(selectedProfile, profile);

            if (ltoken != null)
            {
                Logger.LogInformation("User {UserId} is already authenticated", Context.Interaction.User.Id);
                await Context.Interaction.SendResponseAsync(
                    InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
                await RedeemCodeAsync(code, selectedProfile.LtUid, ltoken, cachedServer.Value);
            }
        }
        catch (CommandException e)
        {
            Logger.LogError(e, "Error processing character command for user {UserId}", Context.Interaction.User.Id);
            await SendErrorMessageAsync(e.Message);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error processing character command for user {UserId}", Context.Interaction.User.Id);
            await SendErrorMessageAsync();
        }
    }

    public override async Task OnAuthenticationCompletedAsync(AuthenticationResult result)
    {
        if (!result.IsSuccess)
        {
            Logger.LogWarning("Authentication failed for user {UserId}: {ErrorMessage}",
                result.UserId, result.ErrorMessage);
            await SendAuthenticationErrorAsync(result.ErrorMessage);
            return;
        }

        if (result.Context != null) Context = result.Context;

        Logger.LogInformation("Authentication completed successfully for user {UserId}", result.UserId);

        await RedeemCodeAsync(m_PendingCode, result.LtUid, result.LToken,
            m_PendingServer!.Value);
    }

    private async ValueTask RedeemCodeAsync(string code, ulong ltuid, string ltoken, Regions server)
    {
        try
        {
            var region = server.GetRegion();
            var user = await UserRepository.GetUserAsync(Context.Interaction.User.Id);

            var result = await GetAndUpdateGameUidAsync(user, GameName.Genshin, ltuid, ltoken, server, region);
            if (!result.IsSuccess) return;

            var gameUid = result.Data;
            var response = await m_ApiService.RedeemCodeAsync(code, region, gameUid, ltuid, ltoken);
            if (response.IsSuccess)
            {
                Logger.LogInformation("Successfully redeemed code {Code} for user {UserId}", code,
                    Context.Interaction.User.Id);
                await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                    .WithFlags(MessageFlags.IsComponentsV2 | MessageFlags.Ephemeral)
                    .AddComponents(new TextDisplayProperties($"Code redeemed successfully: {code}")));
                BotMetrics.TrackCommand(Context.Interaction.User, "genshin codes", true);
            }
            else
            {
                Logger.LogError("Failed to redeem code {Code} for user {UserId}: {ErrorMessage}", code,
                    Context.Interaction.User.Id, response.ErrorMessage);
                await SendErrorMessageAsync(response.ErrorMessage);
                BotMetrics.TrackCommand(Context.Interaction.User, "genshin codes", false);
            }
        }
        catch (CommandException e)
        {
            Logger.LogError(e, "Error redeeming code {Code} for user {UserId}", code, Context.Interaction.User.Id);
            await SendErrorMessageAsync(e.Message);
            BotMetrics.TrackCommand(Context.Interaction.User, "genshin codes", false);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error redeeming code {Code} for user {UserId}", code, Context.Interaction.User.Id);
            await SendErrorMessageAsync("An error occurred while redeeming the code");
            BotMetrics.TrackCommand(Context.Interaction.User, "genshin codes", false);
        }
    }
}
