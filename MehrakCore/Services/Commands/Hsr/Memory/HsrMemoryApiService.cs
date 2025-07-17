#region

using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using MehrakCore.ApiResponseTypes.Hsr;
using MehrakCore.Models;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;

#endregion

namespace MehrakCore.Services.Commands.Hsr.Memory;

internal class HsrMemoryApiService : IApiService<HsrMemoryCommandExecutor>
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<HsrMemoryApiService> m_Logger;
    private const string ApiUrl = "https://sg-public-api.hoyolab.com/event/game_record/hkrpg/api/challenge";

    public HsrMemoryApiService(IHttpClientFactory httpClientFactory, ILogger<HsrMemoryApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async ValueTask<ApiResult<HsrMemoryInformation>> GetMemoryInformationAsync(string gameUid, string region,
        ulong ltuid, string ltoken)
    {
        try
        {
            var client = m_HttpClientFactory.CreateClient("Default");
            var request = new HttpRequestMessage(HttpMethod.Get,
                $"{ApiUrl}?role_id={gameUid}&server={region}&schedule_type=1&need_all=true");
            request.Headers.Add("Cookie", $"ltuid_v2={ltuid}; ltoken_v2={ltoken}");
            request.Headers.Add("Ds", DSGenerator.GenerateDS());
            request.Headers.Add("X-Rpc-App_version", "1.5.0");
            request.Headers.Add("X-Rpc-Language", "en-us");
            request.Headers.Add("X-Rpc-Client_type", "5");
            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError("Failed to fetch Memory of Chaos information for gameUid: {GameUid}", gameUid);
                return ApiResult<HsrMemoryInformation>.Failure(response.StatusCode,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later");
            }

            var json = await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync());
            if (json == null)
            {
                m_Logger.LogError("Failed to fetch Memory of Chaos information for gameUid: {GameUid}", gameUid);
                return ApiResult<HsrMemoryInformation>.Failure(HttpStatusCode.InternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later");
            }

            if (json["retcode"]?.GetValue<int>() == 10001)
            {
                m_Logger.LogError("Invalid cookies for gameUid: {GameUid}", gameUid);
                return ApiResult<HsrMemoryInformation>.Failure(HttpStatusCode.Unauthorized,
                    "Invalid HoYoLAB UID or Cookies. Please authenticate again.");
            }

            if (json["retcode"]?.GetValue<int>() != 0)
            {
                m_Logger.LogError(
                    "Failed to fetch Memory of Chaos information for gameUid: {GameUid}, retcode: {Retcode}",
                    gameUid, json["retcode"]);
                return ApiResult<HsrMemoryInformation>.Failure(HttpStatusCode.InternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later");
            }

            var memoryInfo = json["data"]?.Deserialize<HsrMemoryInformation>()!;

            return ApiResult<HsrMemoryInformation>.Success(memoryInfo);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Failed to get Memory of Chaos information for gameUid: {GameUid}, region: {Region}",
                gameUid, region);
            return ApiResult<HsrMemoryInformation>.Failure(HttpStatusCode.InternalServerError,
                "An error occurred while fetching Memory of Chaos information");
        }
    }
}
