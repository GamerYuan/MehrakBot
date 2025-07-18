﻿#region

using System.Net;
using System.Text.Json.Nodes;
using MehrakCore.Models;
using MehrakCore.Modules;
using Microsoft.Extensions.Logging;

#endregion

namespace MehrakCore.Services.Commands.Zzz.CodeRedeem;

public class ZzzCodeRedeemApiService : ICodeRedeemApiService<ZzzCommandModule>
{
    private const string ApiUrl = "https://public-operation-nap.hoyolab.com/common/apicdkey/api/webExchangeCdkeyHyl";

    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<ZzzCodeRedeemApiService> m_Logger;

    public ZzzCodeRedeemApiService(IHttpClientFactory httpClientFactory,
        ILogger<ZzzCodeRedeemApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async ValueTask<ApiResult<string>> RedeemCodeAsync(string code, string region, string gameUid, ulong ltuid,
        string ltoken)
    {
        try
        {
            var client = m_HttpClientFactory.CreateClient("Default");

            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Get,
                RequestUri =
                    new Uri($"{ApiUrl}?cdkey={code}&game_biz=nap_global&region={region}&uid={gameUid}&lang=en-us"),
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
                return ApiResult<string>.Failure(HttpStatusCode.InternalServerError,
                    "An error occurred while redeeming the code");
            }

            var json = await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync());
            if (json == null)
            {
                m_Logger.LogError("Failed to parse JSON response for code {Code} and uid {GameUid}", code, gameUid);
                return ApiResult<string>.Failure(HttpStatusCode.InternalServerError,
                    "An error occurred while redeeming the code");
            }

            var retCode = json["retcode"]?.GetValue<int>() ?? -1;
            return retCode switch
            {
                0 => ApiResult<string>.Success("Redeemed Successfully!"),
                -2001 => ApiResult<string>.Success("Redemption Code Expired", retCode),
                -2003 => ApiResult<string>.Success("Invalid Code", retCode),
                -2016 => ApiResult<string>.Success("Redemption in Cooldown", retCode),
                -2017 => ApiResult<string>.Success("Redemption Code Already Used", retCode),
                _ => ApiResult<string>.Failure(HttpStatusCode.InternalServerError,
                    "An error occurred while redeeming the code")
            };
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "An error occurred while redeeming code {Code} for gameUid {GameUid}", code, gameUid);
            return ApiResult<string>.Failure(HttpStatusCode.InternalServerError,
                "An error occurred while redeeming the code");
        }
    }
}
