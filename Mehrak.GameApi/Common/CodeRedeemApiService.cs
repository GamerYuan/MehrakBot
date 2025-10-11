using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;

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
            HttpClient client = m_HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new()
            {
                Method = HttpMethod.Get,
                RequestUri =
                    new Uri($"{GetUri(context.Game)}?cdkey={context.Code}&game_biz={context.Game.ToGameBizString()}" +
                        $"&region={context.Region}&uid={context.GameUid}&lang=en-us"),
                Headers =
                {
                    { "Cookie", $"ltuid_v2={context.LtUid}; ltoken_v2={context.LToken}" }
                }
            };

            HttpResponseMessage response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError("Failed to redeem code {Code} for uid {GameUid}. Status code: {StatusCode}",
                    context.Code, context.GameUid, response.StatusCode);
                return Result<CodeRedeemResult>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while redeeming the code");
            }

            JsonNode? json = await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync());
            if (json == null)
            {
                m_Logger.LogError("Failed to parse JSON response for code {Code} and uid {GameUid}", context.Code, context.GameUid);
                return Result<CodeRedeemResult>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while redeeming the code");
            }

            int retCode = json["retcode"]?.GetValue<int>() ?? -1;
            return retCode switch
            {
                0 => Result<CodeRedeemResult>.Success(new("Redeemed Successfully!", CodeStatus.Valid)),
                -2001 => Result<CodeRedeemResult>.Success(new("Redemption Code Expired", CodeStatus.Invalid)),
                -2003 => Result<CodeRedeemResult>.Success(new("Invalid Code", CodeStatus.Invalid)),
                -2016 => Result<CodeRedeemResult>.Success(new("Redemption in Cooldown", CodeStatus.Valid)),
                -2017 => Result<CodeRedeemResult>.Success(new("Redemption Code Already Used", CodeStatus.Valid)),
                _ => Result<CodeRedeemResult>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while redeeming the code")
            };
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "An error occurred while redeeming code {Code} for gameUid {GameUid}", context.Code, context.GameUid);
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
