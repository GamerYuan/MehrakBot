#region

using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Genshin.Types;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;

#endregion

namespace Mehrak.GameApi.Genshin;

internal class GenshinTheaterApiService : IApiService<GenshinTheaterInformation>
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<GenshinTheaterApiService> m_Logger;

    private static readonly string ApiEndpoint = "/event/game_record/genshin/api/role_combat";

    public GenshinTheaterApiService(IHttpClientFactory httpClientFactory, ILogger<GenshinTheaterApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<GenshinTheaterInformation>> GetAsync(ulong ltuid, string ltoken,
        string gameUid = "", string region = "")
    {
        if (string.IsNullOrEmpty(gameUid) || string.IsNullOrEmpty(region))
        {
            m_Logger.LogError("Game UID or region is null or empty");
            return Result<GenshinTheaterInformation>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

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
                return Result<GenshinTheaterInformation>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while retrieving Imaginarium Theater data");
            }

            var json = await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync());
            if (json == null)
            {
                m_Logger.LogError("Failed to parse Theater data for gameUid: {GameUid}", gameUid);
                return Result<GenshinTheaterInformation>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while retrieving Imaginarium Theater data");
            }

            if (json["retcode"]?.GetValue<int>() == 10001)
            {
                m_Logger.LogError("Invalid cookies for gameUid: {GameUid}", gameUid);
                return Result<GenshinTheaterInformation>.Failure(StatusCode.Unauthorized,
                    "Invalid HoYoLAB UID or Cookies. Please authenticate again");
            }

            if (json["retcode"]?.GetValue<int>() != 0)
            {
                m_Logger.LogError("Failed to fetch Theater data for gameUid: {GameUid}, retcode: {Retcode}",
                    gameUid, json["retcode"]?.GetValue<int>());
                return Result<GenshinTheaterInformation>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while retrieving Imaginarium Theater data");
            }

            // TODO: Update this logic
            if (json["data"]?["is_unlock"]?.GetValue<bool>() == false)
            {
                m_Logger.LogInformation("Imaginarium Theater is not unlocked for gameUid: {GameUid}", gameUid);
                return Result<GenshinTheaterInformation>.Failure(StatusCode.ExternalServerError,
                    "Imaginarium Theater is not unlocked yet");
            }

            var theaterInfo = json["data"]?["data"]?.Deserialize<GenshinTheaterInformation[]>();
            if (theaterInfo == null || theaterInfo.Length == 0)
            {
                m_Logger.LogError("No Theater data found for gameUid: {GameUid}", gameUid);
                return Result<GenshinTheaterInformation>.Failure(StatusCode.ExternalServerError,
                    "No Imaginarium Theater data found");
            }

            var theaterData = theaterInfo[0];
            if (!theaterData.HasDetailData || theaterData.Schedule.ScheduleType != 1)
            {
                m_Logger.LogError("No Theater data found for this cycle for gameUid: {GameUid}", gameUid);
                return Result<GenshinTheaterInformation>.Failure(StatusCode.ExternalServerError,
                    "No Imaginarium Theater data found for this cycle");
            }

            return Result<GenshinTheaterInformation>.Success(theaterData);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Failed to get Theater data for gameUid: {GameUid}, region: {Region}",
                gameUid, region);
            return Result<GenshinTheaterInformation>.Failure(StatusCode.BotError,
                "An error occurred while retrieving Imaginarium Theater data");
        }
    }

    public async Task<Result<Dictionary<string, Stream>>> GetBuffIconsAsync(SplendourBuff buffs)
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

        if (isSuccess) return Result<Dictionary<string, Stream>>.Success(dict);

        m_Logger.LogError("Failed to fetch some buff icons");
        return Result<Dictionary<string, Stream>>.Failure(StatusCode.BotError,
            "An error occurred while retrieving Imaginarium Theater data");
    }
}
