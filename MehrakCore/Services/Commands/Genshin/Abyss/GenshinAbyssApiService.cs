#region

using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Models;
using Microsoft.Extensions.Logging;

#endregion

namespace MehrakCore.Services.Commands.Genshin.Abyss;

internal class GenshinAbyssApiService : IApiService<GenshinAbyssCommandExecutor>
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<GenshinAbyssApiService> m_Logger;
    private const string ApiUrl = "https://sg-public-api.hoyolab.com/event/game_record/genshin/api/spiralAbyss";

    public GenshinAbyssApiService(IHttpClientFactory httpClientFactory, ILogger<GenshinAbyssApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async ValueTask<ApiResult<GenshinAbyssInformation>> GetAbyssInformationAsync(string gameUid, string region,
        ulong ltuid, string ltoken)
    {
        try
        {
            var client = m_HttpClientFactory.CreateClient("Default");
            var request = new HttpRequestMessage(HttpMethod.Get,
                $"{ApiUrl}?role_id={gameUid}&server={region}&schedule_type=1");
            request.Headers.Add("Cookie", $"ltuid_v2={ltuid}; ltoken_v2={ltoken}");
            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError("Failed to fetch Abyss information for gameUid: {GameUid}", gameUid);
                return ApiResult<GenshinAbyssInformation>.Failure(response.StatusCode,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later");
            }

            var json = await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync());
            if (json == null)
            {
                m_Logger.LogError("Failed to fetch Abyss information for gameUid: {GameUid}", gameUid);
                return ApiResult<GenshinAbyssInformation>.Failure(HttpStatusCode.InternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later");
            }

            if (json["retcode"]?.GetValue<int>() == 10001)
            {
                m_Logger.LogError("Invalid cookies for gameUid: {GameUid}", gameUid);
                return ApiResult<GenshinAbyssInformation>.Failure(HttpStatusCode.Unauthorized,
                    "Invalid HoYoLAB UID or Cookies. Please authenticate again.");
            }

            if (json["retcode"]?.GetValue<int>() != 0)
            {
                m_Logger.LogError("Failed to fetch Abyss information for gameUid: {GameUid}, retcode: {Retcode}",
                    gameUid, json["retcode"]);
                return ApiResult<GenshinAbyssInformation>.Failure(HttpStatusCode.InternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later");
            }

            var abyssInfo = json["data"]?.Deserialize<GenshinAbyssInformation>()!;

            return ApiResult<GenshinAbyssInformation>.Success(abyssInfo);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Failed to get Abyss information for gameUid: {GameUid}, region: {Region}",
                gameUid, region);
            return ApiResult<GenshinAbyssInformation>.Failure(HttpStatusCode.InternalServerError,
                "An unknown error occurred. Please try again later");
        }
    }
}
