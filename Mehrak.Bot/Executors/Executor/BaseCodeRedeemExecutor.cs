#region

using System.Text;
using Mehrak.Bot.Modules;
using MehrakCore.Models;
using MehrakCore.Services.Common;
using MehrakCore.Services.Metrics;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;

#endregion

namespace Mehrak.Bot.Executors.Executor;

/// <summary>
/// Base class for code redemption executors that provides common functionality for
/// code validation, authentication, and redemption flow while preserving interface-based dependency injection.
/// </summary>
/// <typeparam name="TModule">The command module type</typeparam>
/// <typeparam name="TLogger">The concrete executor type</typeparam>
public abstract class BaseCodeRedeemExecutor<TModule, TLogger> : BaseCommandExecutor<TLogger>,
    ICodeRedeemExecutor<TModule>
    where TModule : ICommandModule
{
    private readonly int m_RedeemDelay = 5500;

    private readonly ICodeRedeemApiService<TModule> m_ApiService;
    private readonly ICodeRedeemRepository m_CodeRedeemRepository;
    private List<string> m_PendingCodes = [];
    private Regions? m_PendingServer;

    protected BaseCodeRedeemExecutor(
        UserRepository userRepository,
        TokenCacheService tokenCacheService,
        IAuthenticationMiddlewareService authenticationMiddleware,
        GameRecordApiService gameRecordApi,
        ICodeRedeemApiService<TModule> apiService,
        ICodeRedeemRepository codeRedeemRepository,
        ILogger<TLogger> logger)
        : base(userRepository, tokenCacheService, authenticationMiddleware, gameRecordApi, logger)
    {
        m_ApiService = apiService;
        m_CodeRedeemRepository = codeRedeemRepository;
    }

    /// <summary>
    /// Gets the game name for the specific implementation.
    /// </summary>
    protected abstract Game Game { get; }

    /// <summary>
    /// Gets the command name for metrics tracking.
    /// </summary>
    protected abstract string CommandName { get; }

    /// <summary>
    /// Gets the region string for the specified server region.
    /// </summary>
    /// <param name="server">The server region</param>
    /// <returns>The region string for API calls</returns>
    protected abstract string GetRegionString(Regions server);

    /// <summary>
    /// Gets the error message to display for generic exceptions.
    /// </summary>
    /// <returns>The error message for generic exceptions</returns>
    protected virtual string GetGenericErrorMessage()
    {
        return "An unknown error occurred while processing your request";
    }

    /// <summary>
    /// Gets whether to use followup for error messages (some implementations require this).
    /// </summary>
    protected virtual bool UseFollowupForErrors => true;

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
            if (codes.Count == 0) codes = await m_CodeRedeemRepository.GetCodesAsync(Game);

            if (codes.Count == 0)
            {
                Logger.LogWarning(
                    "User {UserId} used the code command but no codes were provided or found in the cache",
                    Context.Interaction.User.Id);
                await SendErrorMessageAsync("No known codes found in database. Please provide a valid code",
                    UseFollowupForErrors);
                return;
            }

            var (user, selectedProfile) = await ValidateUserAndProfileAsync(profile);
            if (user == null || selectedProfile == null)
                return;

            var cachedServer = server ?? GetCachedServer(selectedProfile, Game);
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
            Logger.LogError(e, "Error processing code command for user {UserId}", Context.Interaction.User.Id);
            await SendErrorMessageAsync(e.Message, UseFollowupForErrors);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error processing code command for user {UserId}", Context.Interaction.User.Id);
            await SendErrorMessageAsync(followup: UseFollowupForErrors);
        }
    }

    public override async Task OnAuthenticationCompletedAsync(AuthenticationResult result)
    {
        if (!result.IsSuccess)
        {
            Logger.LogWarning("Authentication failed for user {UserId}: {ErrorMessage}",
                result.UserId, result.ErrorMessage);
            return;
        }

        Context = result.Context;

        Logger.LogInformation("Authentication completed successfully for user {UserId}", result.UserId);

        await RedeemCodeAsync(m_PendingCodes, result.LtUid, result.LToken, m_PendingServer!.Value);
    }

    private async ValueTask RedeemCodeAsync(List<string> codes, ulong ltuid, string ltoken, Regions server)
    {
        try
        {
            var region = GetRegionString(server);
            var user = await UserRepository.GetUserAsync(Context.Interaction.User.Id);

            var result = await GetAndUpdateGameUidAsync(user, Game, ltuid, ltoken, server, region);
            if (!result.IsSuccess) return;

            var gameUid = result.Data;
            StringBuilder sb = new();
            Dictionary<string, CodeStatus> successfulCodes = [];

            for (var i = 0; i < codes.Count; i++)
            {
                var code = codes[i];
                var trimmedCode = code.ToUpperInvariant().Trim();
                var response = await m_ApiService.RedeemCodeAsync(trimmedCode, region, gameUid, ltuid, ltoken);
                if (response.IsSuccess)
                {
                    Logger.LogInformation("Successfully redeemed code {Code} for user {UserId}", trimmedCode,
                        Context.Interaction.User.Id);
                    sb.Append($"{trimmedCode}: {response.Data}\n");
                    successfulCodes.Add(trimmedCode, MapRetCodeToStatus(response.RetCode ?? -1));
                }
                else
                {
                    Logger.LogError("Failed to redeem code {Code} for user {UserId}: {ErrorMessage}", trimmedCode,
                        Context.Interaction.User.Id, response.ErrorMessage);
                    throw new CommandException(response.ErrorMessage);
                }

                if (i < codes.Count - 1) await Task.Delay(m_RedeemDelay);
            }

            if (successfulCodes.Count > 0)
                await m_CodeRedeemRepository.AddCodesAsync(Game, successfulCodes).ConfigureAwait(false);

            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithFlags(MessageFlags.IsComponentsV2 | MessageFlags.Ephemeral)
                .AddComponents(new TextDisplayProperties(sb.ToString().TrimEnd())));

            BotMetrics.TrackCommand(Context.Interaction.User, CommandName, true);
        }
        catch (CommandException e)
        {
            Logger.LogError(e, "Error redeeming code {Code} for user {UserId}", codes, Context.Interaction.User.Id);
            await SendErrorMessageAsync(e.Message);
            BotMetrics.TrackCommand(Context.Interaction.User, CommandName, false);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error redeeming code {Code} for user {UserId}", codes, Context.Interaction.User.Id);
            await SendErrorMessageAsync();
            BotMetrics.TrackCommand(Context.Interaction.User, CommandName, false);
        }
    }

    /// <summary>
    /// Maps the API return code to a CodeStatus enum value.
    /// </summary>
    /// <param name="retCode">The return code from the API</param>
    /// <returns>The corresponding CodeStatus</returns>
    private static CodeStatus MapRetCodeToStatus(int retCode)
    {
        return retCode switch
        {
            0 => CodeStatus.Valid,
            -2001 => CodeStatus.Invalid,
            -2003 => CodeStatus.Invalid,
            -2016 => CodeStatus.Valid,
            -2017 => CodeStatus.Valid,
            _ => CodeStatus.Invalid
        };
    }
}
