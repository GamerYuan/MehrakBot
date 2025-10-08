#region

using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Genshin.Types;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

#endregion

namespace Mehrak.GameApi.Genshin;

internal class GenshinTheaterApiService : IApiService<GenshinTheaterCommandExecutor>
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<GenshinTheaterApiService> m_Logger;

    private static readonly string ApiEndpoint = "/event/game_record/genshin/api/role_combat";

    public GenshinTheaterApiService(IHttpClientFactory httpClientFactory, ILogger<GenshinTheaterApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async ValueTask<ApiResult<GenshinTheaterInformation>> GetTheaterDataAsync(string gameUid, string region,
        ulong ltuid, string ltoken)
    {
        try
        {
            var client = m_HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new(HttpMethod.Get,
                $"{HoYoLabDomains.PublicApi}{ApiEndpoint}?role_id={gameUid}&server={region}&need_detail=true");
            request.Headers.Add("Cookie", $"ltuid_v2={ltuid}; ltoken_v2={ltoken}");
            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError("Failed to fetch Theater data for gameUid: {GameUid}", gameUid);
                return ApiResult<GenshinTheaterInformation>.Failure(response.StatusCode,
                    "An error occurred while retrieving Imaginarium Theater data");
            }

            var json = await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync());
            if (json == null)
            {
                m_Logger.LogError("Failed to parse Theater data for gameUid: {GameUid}", gameUid);
                return ApiResult<GenshinTheaterInformation>.Failure(HttpStatusCode.InternalServerError,
                    "An error occurred while retrieving Imaginarium Theater data");
            }

            if (json["retcode"]?.GetValue<int>() == 10001)
            {
                m_Logger.LogError("Invalid cookies for gameUid: {GameUid}", gameUid);
                return ApiResult<GenshinTheaterInformation>.Failure(HttpStatusCode.Unauthorized,
                    "Invalid HoYoLAB UID or Cookies. Please authenticate again");
            }

            if (json["retcode"]?.GetValue<int>() != 0)
            {
                m_Logger.LogError("Failed to fetch Theater data for gameUid: {GameUid}, retcode: {Retcode}",
                    gameUid, json["retcode"]?.GetValue<int>());
                return ApiResult<GenshinTheaterInformation>.Failure(HttpStatusCode.InternalServerError,
                    "An error occurred while retrieving Imaginarium Theater data");
            }

            if (json["data"]?["is_unlock"]?.GetValue<bool>() == false)
            {
                m_Logger.LogInformation("Imaginarium Theater is not unlocked for gameUid: {GameUid}", gameUid);
                return ApiResult<GenshinTheaterInformation>.Failure(HttpStatusCode.NotFound,
                    "Imaginarium Theater is not unlocked yet");
            }

            var theaterInfo = json["data"]?["data"]?.Deserialize<GenshinTheaterInformation[]>();
            if (theaterInfo == null || theaterInfo.Length == 0)
            {
                m_Logger.LogError("No Theater data found for gameUid: {GameUid}", gameUid);
                return ApiResult<GenshinTheaterInformation>.Failure(HttpStatusCode.NotFound,
                    "No Imaginarium Theater data found");
            }

            var theaterData = theaterInfo[0];
            if (!theaterData.HasDetailData || theaterData.Schedule.ScheduleType != 1)
            {
                m_Logger.LogError("No Theater data found for this cycle for gameUid: {GameUid}", gameUid);
                return ApiResult<GenshinTheaterInformation>.Failure(HttpStatusCode.NotFound,
                    "No Imaginarium Theater data found for this cycle");
            }

            return ApiResult<GenshinTheaterInformation>.Success(theaterData);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Failed to get Theater data for gameUid: {GameUid}, region: {Region}",
                gameUid, region);
            return ApiResult<GenshinTheaterInformation>.Failure(HttpStatusCode.InternalServerError,
                "An error occurred while retrieving Imaginarium Theater data");
        }
    }

    public async Task<ApiResult<Dictionary<string, Stream>>> GetBuffIconsAsync(SplendourBuff buffs)
    {
        bool isSuccess = true;
        var dict = await buffs.Buffs.ToAsyncEnumerable().ToDictionaryAwaitAsync(
            async x => await Task.FromResult(x.Name),
            async x =>
            {
                try
                {
                    var client = m_HttpClientFactory.CreateClient("Default");
                    HttpRequestMessage request = new(HttpMethod.Get, x.Icon);
                    var response = await client.SendAsync(request);
                    if (response.IsSuccessStatusCode) return await response.Content.ReadAsStreamAsync();

                    m_Logger.LogError("Failed to fetch buff icon for {BuffName}: {StatusCode}", x.Name,
                        response.StatusCode);
                    isSuccess = false;
                    return new MemoryStream();
                }
                catch
                {
                    isSuccess = false;
                    return new MemoryStream();
                }
            });

        if (isSuccess) return ApiResult<Dictionary<string, Stream>>.Success(dict);

        m_Logger.LogError("Failed to fetch some buff icons");
        return ApiResult<Dictionary<string, Stream>>.Failure(HttpStatusCode.InternalServerError,
            "An error occurred while retrieving Imaginarium Theater data");
    }
}
