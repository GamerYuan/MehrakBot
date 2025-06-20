#region

using MehrakCore.Models;
using MehrakCore.Modules;
using MehrakCore.Repositories;
using MehrakCore.Services.Commands.Executor;
using MehrakCore.Services.Common;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;

#endregion

namespace MehrakCore.Services.Commands.Hsr.CodeRedeem;

public class HsrCodeRedeemExecutor : BaseCommandExecutor<HsrCommandModule>,
    ICodeRedeemExecutor<HsrCommandModule>
{
    private readonly ICodeRedeemApiService<HsrCommandModule> m_ApiService;
    private string m_PendingCode = string.Empty;
    private Regions? m_PendingServer;

    public HsrCodeRedeemExecutor(UserRepository userRepository, TokenCacheService tokenCacheService,
        IAuthenticationMiddlewareService authenticationMiddleware, GameRecordApiService gameRecordApi,
        ICodeRedeemApiService<HsrCommandModule> apiService, ILogger<HsrCommandModule> logger) : base(
        userRepository, tokenCacheService, authenticationMiddleware,
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

            var cachedServer = server ?? GetCachedServer(selectedProfile, GameName.HonkaiStarRail);
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
        catch (Exception e)
        {
            Logger.LogError(e, "Error processing character command for user {UserId}", Context.Interaction.User.Id);
            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                .WithComponents([
                    new TextDisplayProperties(
                        "An error occurred while processing your request. Please try again later.")
                ]));
        }
    }

    public override async Task OnAuthenticationCompletedAsync(AuthenticationResult result)
    {
        try
        {
            if (!result.IsSuccess)
            {
                Logger.LogWarning("Authentication failed for user {UserId}: {ErrorMessage}",
                    result.UserId, result.ErrorMessage);
                await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                    .WithContent($"Authentication failed: {result.ErrorMessage}")
                    .WithFlags(MessageFlags.Ephemeral));
                return;
            }

            // Update context if available
            if (result.Context != null) Context = result.Context;

            // Check if we have the required pending parameters
            if (string.IsNullOrEmpty(m_PendingCode) || !m_PendingServer.HasValue)
            {
                Logger.LogWarning("Missing required parameters for command execution for user {UserId}",
                    result.UserId);
                await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                    .WithContent("Error: Missing required parameters for command execution")
                    .WithFlags(MessageFlags.Ephemeral));
                return;
            }

            Logger.LogInformation("Authentication completed successfully for user {UserId}", result.UserId);

            await RedeemCodeAsync(m_PendingCode, result.LtUid, result.LToken,
                m_PendingServer.Value);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error handling authentication completion for user {UserId}", result.UserId);
            if (Context?.Interaction != null)
                await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                    .WithContent("An error occurred while processing your authentication")
                    .WithFlags(MessageFlags.Ephemeral));
        }
    }

    private async ValueTask RedeemCodeAsync(string code, ulong ltuid, string ltoken, Regions server)
    {
        try
        {
            var region = server.GetRegion();
            var user = await UserRepository.GetUserAsync(Context.Interaction.User.Id);

            var result = await GetAndUpdateGameUidAsync(user, GameName.HonkaiStarRail, ltuid, ltoken, server, region);
            if (!result.IsSuccess)
            {
                Logger.LogError("Failed to get game UID for user {UserId}: {ErrorMessage}",
                    Context.Interaction.User.Id, result.ErrorMessage);
                await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                    .WithFlags(MessageFlags.IsComponentsV2 | MessageFlags.Ephemeral)
                    .AddComponents(new TextDisplayProperties(result.ErrorMessage)));
                return;
            }

            var gameUid = result.Data;
            var response = await m_ApiService.RedeemCodeAsync(code, region, gameUid, ltuid, ltoken);
            if (response.IsSuccess)
            {
                Logger.LogInformation("Successfully redeemed code {Code} for user {UserId}", code,
                    Context.Interaction.User.Id);
                await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                    .WithFlags(MessageFlags.IsComponentsV2 | MessageFlags.Ephemeral)
                    .AddComponents(new TextDisplayProperties($"Code redeemed successfully: {code}")));
            }
            else
            {
                Logger.LogError("Failed to redeem code {Code} for user {UserId}: {ErrorMessage}", code,
                    Context.Interaction.User.Id, response.ErrorMessage);
                await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                    .WithFlags(MessageFlags.IsComponentsV2 | MessageFlags.Ephemeral)
                    .AddComponents(new TextDisplayProperties(response.ErrorMessage)));
            }
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error redeeming code {Code} for user {UserId}", code, Context.Interaction.User.Id);
            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithFlags(MessageFlags.IsComponentsV2 | MessageFlags.Ephemeral)
                .AddComponents(new TextDisplayProperties(
                    "An error occurred while redeeming the code. Please try again later.")));
        }
    }
}
