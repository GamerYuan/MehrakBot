#region

using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.GameApi.Shared;
using Mehrak.GameApi.Shared.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.CodeRedeem;

public class CodeRedeemApiService : IApiService<CodeRedeemResult, CodeRedeemApiContext>
{
    private const string UnsupportedGameMessage = "Code redemption is not supported for this game";

    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<CodeRedeemApiService> m_Logger;

    public CodeRedeemApiService(IHttpClientFactory httpClientFactory, ILogger<CodeRedeemApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<CodeRedeemResult>> GetAsync(CodeRedeemApiContext context, CancellationToken cancellationToken = default)
    {
        if (context.Game is not (Game.Genshin or Game.HonkaiStarRail or Game.ZenlessZoneZero))
        {
            m_Logger.LogError("Code redemption is not supported for game {Game} for User {UserId}", context.Game,
                GameApiLog.ContextId(context.UserId));
            return Result<CodeRedeemResult>.Failure(StatusCode.BadParameter, UnsupportedGameMessage);
        }

        var requestUri = $"{GetUri(context.Game)}?cdkey={context.Code}&game_biz={context.Game.ToGameBizString()}" +
                         $"&region={context.Region}&uid={context.GameUid}&lang=en-us";

        try
        {
            using var scope = new HoYoLabRequestScope(cancellationToken);

            var envelope = await HoYoLabApi.SendEnvelopeAsync<object>(
                new HoYoLabRequest
                {
                    RequestUri = requestUri,
                    Failures = new HoYoLabFailureMessages
                    {
                        Unauthorized = "Invalid HoYoLAB UID or Cookies. Please re-authenticate",
                        Parse = "An error occurred while redeeming the code",
                        NonSuccessStatus = "An error occurred while redeeming the code"
                    }
                },
                new HoYoLabCredentials(context.UserId, context.LtUid, context.LToken),
                m_HttpClientFactory, m_Logger, scope.Token, HoYoLabRetcodes.AccountAuth);

            if (!envelope.IsSuccess)
                return Result<CodeRedeemResult>.Failure(envelope.StatusCode, envelope.ErrorMessage,
                    envelope.RequestUri);

            var json = envelope.Data;

            switch (json.Retcode)
            {
                case HoYoLabRetcodes.AccountAuth:
                    m_Logger.LogError(LogMessages.InvalidCredentials, GameApiLog.ContextId(context.UserId));
                    return Result<CodeRedeemResult>.Failure(StatusCode.Unauthorized,
                        "Invalid HoYoLAB UID or Cookies. Please re-authenticate", requestUri);

                case 0:
                    m_Logger.LogInformation("Successfully redeemed code for User {UserId} in game {Game}",
                        GameApiLog.ContextId(context.UserId), context.Game);
                    return Result<CodeRedeemResult>.Success(new CodeRedeemResult("Redeemed Successfully!",
                        CodeStatus.Valid, RedemptionOutcome.Redeemed), requestUri: requestUri);

                case -2001:
                    m_Logger.LogInformation("Redemption code expired for User {UserId} in game {Game}",
                        GameApiLog.ContextId(context.UserId), context.Game);
                    return Result<CodeRedeemResult>.Success(new CodeRedeemResult("Redemption Code Expired",
                        CodeStatus.Invalid, RedemptionOutcome.Expired), requestUri: requestUri);

                case -2003:
                    m_Logger.LogInformation("Invalid redemption code for User {UserId} in game {Game}",
                        GameApiLog.ContextId(context.UserId), context.Game);
                    return Result<CodeRedeemResult>.Success(new CodeRedeemResult("Invalid Code", CodeStatus.Invalid, RedemptionOutcome.Invalid), requestUri: requestUri);

                case -2016:
                    m_Logger.LogInformation("Redemption in cooldown for User {UserId} in game {Game}",
                        GameApiLog.ContextId(context.UserId), context.Game);
                    return Result<CodeRedeemResult>.Success(new CodeRedeemResult("Redemption in Cooldown",
                        CodeStatus.Valid, RedemptionOutcome.Cooldown), requestUri: requestUri);

                case -2017:
                    m_Logger.LogInformation("Redemption code already used for User {UserId} in game {Game}",
                        GameApiLog.ContextId(context.UserId), context.Game);
                    return Result<CodeRedeemResult>.Success(new CodeRedeemResult("Redemption Code Already Used",
                        CodeStatus.Valid, RedemptionOutcome.AlreadyUsed), requestUri: requestUri);

                default:
                    m_Logger.LogError(LogMessages.UnknownRetcode, json.Retcode, GameApiLog.ContextId(context.UserId),
                        GameApiLog.Endpoint(requestUri), GameApiLog.SafeQueryParameters(requestUri),
                        GameApiLog.RenderBody(json));
                    return Result<CodeRedeemResult>.Failure(StatusCode.ExternalServerError,
                        "An error occurred while redeeming the code", requestUri);
            }
        }
        catch (OperationCanceledException)
        {
            return Result<CodeRedeemResult>.FromCancellation(cancellationToken, requestUri);
        }
        catch (Exception e)
        {
            GameApiLog.LogException(m_Logger, GetUri(context.Game), context.UserId, e);
            return Result<CodeRedeemResult>.Failure(StatusCode.BotError,
                "An error occurred while redeeming the code", requestUri);
        }
    }

    private static string GetUri(Game game)
    {
        return game switch
        {
            Game.Genshin => $"{HoYoLabDomains.GenshinOpsApi}/common/apicdkey/api/webExchangeCdkeyHyl",
            Game.HonkaiStarRail => $"{HoYoLabDomains.HsrOpsApi}/common/apicdkey/api/webExchangeCdkeyHyl",
            Game.ZenlessZoneZero => $"{HoYoLabDomains.ZzzOpsApi}/common/apicdkey/api/webExchangeCdkeyHyl",
            _ => ""
        };
    }
}

public class CodeRedeemApiContext : BaseHoYoApiContext
{
    public Game Game { get; }
    public string Code { get; }

    public CodeRedeemApiContext(ulong userId, ulong ltuid, string lToken,
        string gameUid, string region, Game game, string code)
        : base(userId, ltuid, lToken, gameUid, region)
    {
        Game = game;
        Code = code;
    }
}

public readonly record struct CodeRedeemResult
{
    public string Message { get; init; }
    public CodeStatus Status { get; init; }

    public RedemptionOutcome Outcome { get; init; }

    public CodeRedeemResult(string message, CodeStatus status, RedemptionOutcome outcome = RedemptionOutcome.Unknown)
    {
        Message = message;
        Status = status;
        Outcome = outcome;
    }
}
