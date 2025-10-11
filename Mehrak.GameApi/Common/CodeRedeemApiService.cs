using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;

namespace Mehrak.GameApi.Common;

public class CodeRedeemApiService : ICodeRedeemApiService
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<CodeRedeemApiService> m_Logger;

    public CodeRedeemApiService(IHttpClientFactory httpClientFactory, ILogger<CodeRedeemApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<(string, CodeStatus)>> RedeemCodeAsync(
        Game game, string code, ulong ltuid, string ltoken, string gameUid, string region)
    {
        try
        {
            HttpClient client = m_HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new()
            {
                Method = HttpMethod.Get,
                RequestUri =
                    new Uri($"{GetUri(game)}?cdkey={code}&game_biz={game.ToGameBizString()}&region={region}&uid={gameUid}&lang=en-us"),
                Headers =
                {
                    { "Cookie", $"ltuid_v2={ltuid}; ltoken_v2={ltoken}" }
                }
            };

            HttpResponseMessage response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError("Failed to redeem code {Code} for uid {GameUid}. Status code: {StatusCode}",
                    code, gameUid, response.StatusCode);
                return Result<(string, CodeStatus)>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while redeeming the code");
            }

            JsonNode? json = await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync());
            if (json == null)
            {
                m_Logger.LogError("Failed to parse JSON response for code {Code} and uid {GameUid}", code, gameUid);
                return Result<(string, CodeStatus)>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while redeeming the code");
            }

            int retCode = json["retcode"]?.GetValue<int>() ?? -1;
            return retCode switch
            {
                0 => Result<(string, CodeStatus)>.Success(("Redeemed Successfully!", CodeStatus.Valid)),
                -2001 => Result<(string, CodeStatus)>.Success(("Redemption Code Expired", CodeStatus.Invalid)),
                -2003 => Result<(string, CodeStatus)>.Success(("Invalid Code", CodeStatus.Invalid)),
                -2016 => Result<(string, CodeStatus)>.Success(("Redemption in Cooldown", CodeStatus.Valid)),
                -2017 => Result<(string, CodeStatus)>.Success(("Redemption Code Already Used", CodeStatus.Valid)),
                _ => Result<(string, CodeStatus)>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while redeeming the code")
            };
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "An error occurred while redeeming code {Code} for gameUid {GameUid}", code, gameUid);
            return Result<(string, CodeStatus)>.Failure(StatusCode.BotError,
                "An error occurred while redeeming the code");
        }
    }

    private static string GetUri(Game game)
    {
        return game switch
        {
            Game.Genshin => $"{HoYoLabDomains.GenshinOpsApi}/common/apicdkey/api/webExchangeCdkeyHyl",
            Game.HonkaiStarRail => $"{HoYoLabDomains.HsrOpsApi}/common/apicdkey/api/webExchangeCdkeyHyl",
            Game.ZenlessZoneZero => $"{HoYoLabDomains.ZzzOpsApi}/common/apicdkey/api/webExchangeCdkeyHyl"
            _ => ""
        };
    }
}
