#region

using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using MehrakCore.ApiResponseTypes.Hsr;
using MehrakCore.Models;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;

#endregion

namespace MehrakCore.Services.Commands.Hsr.PureFiction;

internal class HsrPureFictionApiService : IApiService<HsrPureFictionCommandExecutor>
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<HsrPureFictionApiService> m_Logger;

    private const string ApiUrl = "https://sg-public-api.hoyolab.com/event/game_record/hkrpg/api/challenge_story";

    public HsrPureFictionApiService(IHttpClientFactory httpClientFactory, ILogger<HsrPureFictionApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async ValueTask<ApiResult<HsrPureFictionInformation>> GetPureFictionDataAsync(string gameUid, string region,
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
                m_Logger.LogError("Failed to fetch Pure Fiction information for gameUid: {GameUid}", gameUid);
                return ApiResult<HsrPureFictionInformation>.Failure(response.StatusCode,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later");
            }

            var json = await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync());
            if (json == null)
            {
                m_Logger.LogError("Failed to fetch Pure Fiction information for gameUid: {GameUid}", gameUid);
                return ApiResult<HsrPureFictionInformation>.Failure(HttpStatusCode.InternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later");
            }

            if (json["retcode"]?.GetValue<int>() == 10001)
            {
                m_Logger.LogError("Invalid cookies for gameUid: {GameUid}", gameUid);
                return ApiResult<HsrPureFictionInformation>.Failure(HttpStatusCode.Unauthorized,
                    "Invalid HoYoLAB UID or Cookies. Please authenticate again.");
            }

            if (json["retcode"]?.GetValue<int>() != 0)
            {
                m_Logger.LogError(
                    "Failed to fetch Pure Fiction information for gameUid: {GameUid}, retcode: {Retcode}",
                    gameUid, json["retcode"]);
                return ApiResult<HsrPureFictionInformation>.Failure(HttpStatusCode.InternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later");
            }

            var pureFictionInfo = json["data"]?.Deserialize<HsrPureFictionInformation>()!;

            return ApiResult<HsrPureFictionInformation>.Success(pureFictionInfo);
        }
        catch
        {
            m_Logger.LogError("Failed to get Pure Fiction data for gameUid: {GameUid}, region: {Region}", gameUid,
                region);
            return ApiResult<HsrPureFictionInformation>.Failure(HttpStatusCode.InternalServerError,
                "An unknown error occurred while fetching Pure Fiction information");
        }
    }

    public async ValueTask<Dictionary<int, Stream>> GetBuffMapAsync(
        HsrPureFictionInformation fictionData)
    {
        return await fictionData.AllFloorDetail.Where(x => !x.IsFast)
            .SelectMany(x => new List<FictionBuff> { x.Node1!.Buff, x.Node2!.Buff })
            .DistinctBy(x => x.Id).ToAsyncEnumerable().ToDictionaryAwaitAsync(
                async x => await Task.FromResult(x.Id),
                async x =>
                {
                    var client = m_HttpClientFactory.CreateClient("Default");
                    var response = await client.GetAsync(x.Icon);
                    return await response.Content.ReadAsStreamAsync();
                });
    }
}
