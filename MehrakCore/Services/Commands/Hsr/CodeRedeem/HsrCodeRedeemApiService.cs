#region

using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json.Nodes;
using MehrakCore.Models;
using MehrakCore.Modules;
using Microsoft.Extensions.Logging;

#endregion

namespace MehrakCore.Services.Commands.Hsr.CodeRedeem;

public class HsrCodeRedeemApiService : ICodeRedeemApiService<GenshinCommandModule>
{
    private const string ApiUrl = "https://public-operation-hkrpg.hoyolab.com/common/apicdkey/api/webExchangeCdkeyHyl";

    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<HsrCodeRedeemApiService> m_Logger;

    public HsrCodeRedeemApiService(IHttpClientFactory httpClientFactory,
        ILogger<HsrCodeRedeemApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async ValueTask<ApiResult<string>> RedeemCodeAsync(string code, string region, string gameUid, ulong ltuid,
        string ltoken)
    {
        var client = m_HttpClientFactory.CreateClient("Default");

        var request = new HttpRequestMessage
        {
            Method = HttpMethod.Get,
            RequestUri =
                new Uri($"{ApiUrl}?cdkey={code}&game_biz=hkrpg_global&region={region}&uid={gameUid}&lang=en-us"),
            Headers =
            {
                { "Cookie", $"ltuid_v2={ltuid}; ltoken_v2={ltoken}" }
            }
        };

        var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            m_Logger.LogError("Failed to redeem code {Code} for uid {GameUid}. Status code: {StatusCode}",
                code, gameUid, response.StatusCode);
            return ApiResult<string>.Failure(HttpStatusCode.InternalServerError, "API returned an error");
        }

        var json = await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync());
        if (json == null)
        {
            m_Logger.LogError("Failed to parse JSON response for code {Code} and uid {GameUid}", code, gameUid);
            return ApiResult<string>.Failure(HttpStatusCode.InternalServerError, "Failed to parse API response");
        }

        var retCode = json["retcode"]?.GetValue<int>() ?? -1;
        return retCode switch
        {
            0 => ApiResult<string>.Success("Redeemed Successfully!"),
            -2001 => ApiResult<string>.Failure(HttpStatusCode.Unauthorized, "Redemption Code Expired"),
            -2003 => ApiResult<string>.Failure(HttpStatusCode.Unauthorized, "Invalid Code"),
            -2016 => ApiResult<string>.Failure(HttpStatusCode.Unauthorized, "Redemption in Cooldown"),
            -2017 => ApiResult<string>.Failure(HttpStatusCode.Unauthorized, "Redemption Code Already Used"),
            _ => ApiResult<string>.Failure(HttpStatusCode.InternalServerError,
                $"API returned an error: {json["message"]?.ToString() ?? "Unknown error"}")
        };
    }

    public async Task<IEnumerable<(string, bool)>> GetApiStatusAsync()
    {
        var ping = new Ping();
        var reply = await ping.SendPingAsync("public-operation-hkrpg.hoyolab.com", 1000);
        return [("Honkai: Star Rail: ", reply.Status == IPStatus.Success)];
    }
}
