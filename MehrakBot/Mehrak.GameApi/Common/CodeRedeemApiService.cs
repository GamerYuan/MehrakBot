#region

using System.Text.Json.Nodes;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.Common;

public class CodeRedeemApiService : IApiService<CodeRedeemResult, CodeRedeemApiContext>
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<CodeRedeemApiService> m_Logger;

    public CodeRedeemApiService(IHttpClientFactory httpClientFactory, ILogger<CodeRedeemApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<CodeRedeemResult>> GetAsync(CodeRedeemApiContext context)
    {
        try
        {
            var requestUri = $"{GetUri(context.Game)}?cdkey={context.Code}&game_biz={context.Game.ToGameBizString()}" +
                             $"&region={context.Region}&uid={context.GameUid}&lang=en-us";

            m_Logger.LogInformation(LogMessages.ReceivedRequest, requestUri);

            HttpClient client = m_HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new()
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri(requestUri),
                Headers =
                {
                    { "Cookie", $"ltuid_v2={context.LtUid}; ltoken_v2={context.LToken}" }
                }
            };

            // Info-level outbound request (no headers)
            m_Logger.LogInformation(LogMessages.OutboundHttpRequest, request.Method, requestUri);
            HttpResponseMessage response = await client.SendAsync(request);

            // Info-level inbound response (status only)
            m_Logger.LogInformation(LogMessages.InboundHttpResponse, (int)response.StatusCode, requestUri);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError(LogMessages.NonSuccessStatusCode, response.StatusCode, requestUri);
                return Result<CodeRedeemResult>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while redeeming the code", requestUri);
            }

            JsonNode? json = await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync());
            if (json == null)
            {
                m_Logger.LogError(LogMessages.FailedToParseResponse, requestUri, context.UserId);
                return Result<CodeRedeemResult>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while redeeming the code", requestUri);
            }

            int retCode = json["retcode"]?.GetValue<int>() ?? -1;

            // Info-level API retcode after parse
            m_Logger.LogInformation(LogMessages.InboundHttpResponseWithRetcode, (int)response.StatusCode, requestUri, retCode, context.UserId);

            switch (retCode)
            {
                case 0:
                    m_Logger.LogInformation("Successfully redeemed code {Code} for User {UserId} gameUid {GameUid}", context.Code,
                        context.UserId, context.GameUid);
                    return Result<CodeRedeemResult>.Success(new CodeRedeemResult("Redeemed Successfully!",
                        CodeStatus.Valid), requestUri: requestUri);

                case -2001:
                    m_Logger.LogInformation("Code {Code} is expired for User {UserId} gameUid {GameUid}", context.Code,
                        context.UserId, context.GameUid);
                    return Result<CodeRedeemResult>.Success(new CodeRedeemResult("Redemption Code Expired",
                        CodeStatus.Invalid), requestUri: requestUri);

                case -2003:
                    m_Logger.LogInformation("Invalid code {Code} for User {UserId} gameUid {GameUid}", context.Code,
                        context.UserId, context.GameUid);
                    return Result<CodeRedeemResult>.Success(new CodeRedeemResult("Invalid Code", CodeStatus.Invalid), requestUri: requestUri);

                case -2016:
                    m_Logger.LogInformation("Redemption in cooldown code {Code} User {UserId} gameUid {GameUid}",
                        context.Code, context.UserId, context.GameUid);
                    return Result<CodeRedeemResult>.Success(new CodeRedeemResult("Redemption in Cooldown",
                        CodeStatus.Valid), requestUri: requestUri);

                case -2017:
                    m_Logger.LogInformation("Code {Code} already used for User {UserId} gameUid {GameUid}", context.Code,
                        context.UserId, context.GameUid);
                    return Result<CodeRedeemResult>.Success(new CodeRedeemResult("Redemption Code Already Used",
                        CodeStatus.Valid), requestUri: requestUri);

                default:
                    m_Logger.LogError(LogMessages.UnknownRetcode, retCode, context.UserId, requestUri);
                    return Result<CodeRedeemResult>.Failure(StatusCode.ExternalServerError,
                        "An error occurred while redeeming the code", requestUri);
            }
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, LogMessages.ExceptionOccurred, GetUri(context.Game), context.UserId);
            return Result<CodeRedeemResult>.Failure(StatusCode.BotError,
                "An error occurred while redeeming the code");
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

public struct CodeRedeemResult
{
    public string Message { get; init; }
    public CodeStatus Status { get; init; }

    public CodeRedeemResult(string message, CodeStatus status)
    {
        Message = message;
        Status = status;
    }
}
