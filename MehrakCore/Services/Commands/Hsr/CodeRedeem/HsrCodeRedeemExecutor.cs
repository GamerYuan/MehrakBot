#region

using System.Text;
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

namespace MehrakCore.Services.Commands.Hsr.CodeRedeem;

public class HsrCodeRedeemExecutor : BaseCommandExecutor<HsrCommandModule>,
    ICodeRedeemExecutor<HsrCommandModule>
{
    private readonly ICodeRedeemApiService<HsrCommandModule> m_ApiService;
    private readonly ICodeRedeemRepository m_CodeRedeemRepository;
    private List<string> m_PendingCodes = [];
    private Regions? m_PendingServer;

    public HsrCodeRedeemExecutor(UserRepository userRepository, TokenCacheService tokenCacheService,
        IAuthenticationMiddlewareService authenticationMiddleware, GameRecordApiService gameRecordApi,
        ICodeRedeemApiService<HsrCommandModule> apiService, ICodeRedeemRepository codeRedeemRepository,
        ILogger<HsrCommandModule> logger) : base(
        userRepository, tokenCacheService, authenticationMiddleware,
        gameRecordApi, logger)
    {
        m_ApiService = apiService;
        m_CodeRedeemRepository = codeRedeemRepository;
    }

    public override async ValueTask ExecuteAsync(params object?[] parameters)
    {
        if (parameters.Length != 3) throw new ArgumentException("Invalid number of parameters provided.");

        var input = (string)parameters[0]!;
        var server = (Regions?)parameters[1];
        var profile = parameters[2] == null ? 1 : (uint)parameters[2]!;

        try
        {
            Logger.LogInformation("User {UserId} used the code command", Context.Interaction.User.Id);
            var codes = RegexExpressions.RedeemCodeSplitRegex().Split(input).Where(x => !string.IsNullOrEmpty(x))
                .ToList();
            if (codes.Count == 0) codes = await m_CodeRedeemRepository.GetCodesAsync(GameName.HonkaiStarRail);

            if (codes.Count == 0)
            {
                Logger.LogWarning(
                    "User {UserId} used the code command but no codes were provided or found in the cache",
                    Context.Interaction.User.Id);
                await SendErrorMessageAsync("No known codes found in database. Please provide a valid code");
                return;
            }

            var (user, selectedProfile) = await ValidateUserAndProfileAsync(profile);
            if (user == null || selectedProfile == null)
                return;

            var cachedServer = server ?? GetCachedServer(selectedProfile, GameName.HonkaiStarRail);
            if (!await ValidateServerAsync(cachedServer))
                return;

            m_PendingCodes = codes;
            m_PendingServer = cachedServer!.Value;

            var ltoken = await GetOrRequestAuthenticationAsync(selectedProfile, profile);

            if (ltoken != null)
            {
                Logger.LogInformation("User {UserId} is already authenticated", Context.Interaction.User.Id);
                await Context.Interaction.SendResponseAsync(
                    InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
                await RedeemCodeAsync(codes, selectedProfile.LtUid, ltoken, cachedServer.Value);
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

        Context = result.Context;

        Logger.LogInformation("Authentication completed successfully for user {UserId}", result.UserId);

        await RedeemCodeAsync(m_PendingCodes, result.LtUid, result.LToken,
            m_PendingServer!.Value);
    }

    private async ValueTask RedeemCodeAsync(List<string> codes, ulong ltuid, string ltoken, Regions server)
    {
        try
        {
            var region = server.GetRegion();
            var user = await UserRepository.GetUserAsync(Context.Interaction.User.Id);

            var result = await GetAndUpdateGameUidAsync(user, GameName.HonkaiStarRail, ltuid, ltoken, server, region);
            if (!result.IsSuccess) return;

            var gameUid = result.Data;
            StringBuilder sb = new();
            Dictionary<string, CodeStatus> successfulCodes = [];

            foreach (var code in codes)
            {
                var trimmedCode = code.ToUpperInvariant().Trim();
                var response =
                    await m_ApiService.RedeemCodeAsync(trimmedCode, region, gameUid, ltuid, ltoken);
                if (response.IsSuccess)
                {
                    Logger.LogInformation("Successfully redeemed code {Code} for user {UserId}", trimmedCode,
                        Context.Interaction.User.Id);
                    sb.Append($"{trimmedCode}: {response.Data}\n");
                    successfulCodes.Add(trimmedCode, response.RetCode switch
                    {
                        0 => CodeStatus.Valid,
                        -2001 => CodeStatus.Invalid,
                        -2003 => CodeStatus.Invalid,
                        -2016 => CodeStatus.Valid,
                        -2017 => CodeStatus.Valid,
                        _ => CodeStatus.Invalid
                    });
                }
                else
                {
                    Logger.LogError("Failed to redeem code {Code} for user {UserId}: {ErrorMessage}", trimmedCode,
                        Context.Interaction.User.Id, response.ErrorMessage);
                    throw new CommandException(response.ErrorMessage);
                }

                await Task.Delay(3500);
            }

            if (successfulCodes.Count > 0)
                await m_CodeRedeemRepository.AddCodesAsync(GameName.HonkaiStarRail, successfulCodes)
                    .ConfigureAwait(false);

            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithFlags(MessageFlags.IsComponentsV2 | MessageFlags.Ephemeral)
                .AddComponents(new TextDisplayProperties(sb.ToString().TrimEnd())));

            BotMetrics.TrackCommand(Context.Interaction.User, "hsr codes", true);
        }
        catch (CommandException e)
        {
            Logger.LogError(e, "Error redeeming code {Code} for user {UserId}", codes, Context.Interaction.User.Id);
            await SendErrorMessageAsync(e.Message);
            BotMetrics.TrackCommand(Context.Interaction.User, "hsr codes", false);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error redeeming code {Code} for user {UserId}", codes, Context.Interaction.User.Id);
            await SendErrorMessageAsync();
            BotMetrics.TrackCommand(Context.Interaction.User, "hsr codes", false);
        }
    }
}
