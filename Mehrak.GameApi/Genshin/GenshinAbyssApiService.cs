#region

using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Genshin.Types;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;

#endregion

namespace Mehrak.GameApi.Genshin;

internal class GenshinAbyssApiService : IApiService<GenshinAbyssInformation>
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<GenshinAbyssApiService> m_Logger;
    private static readonly string ApiEndpoint = "/event/game_record/genshin/api/spiralAbyss";

    public GenshinAbyssApiService(IHttpClientFactory httpClientFactory, ILogger<GenshinAbyssApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<GenshinAbyssInformation>> GetAsync(ulong ltuid, string ltoken,
        string gameUid = "", string region = "")
    {
        if (string.IsNullOrEmpty(gameUid) || string.IsNullOrEmpty(region))
        {
            m_Logger.LogError("Game UID or region is null or empty");
            return Result<GenshinAbyssInformation>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        try
        {
            var client = m_HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new(HttpMethod.Get,
                $"{HoYoLabDomains.PublicApi}{ApiEndpoint}?role_id={gameUid}&server={region}&schedule_type=1");
            request.Headers.Add("Cookie", $"ltuid_v2={ltuid}; ltoken_v2={ltoken}");
            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError("Failed to fetch Abyss information for gameUid: {GameUid}", gameUid);
                return Result<GenshinAbyssInformation>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later");
            }

            var json = await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync());
            if (json == null)
            {
                m_Logger.LogError("Failed to fetch Abyss information for gameUid: {GameUid}", gameUid);
                return Result<GenshinAbyssInformation>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later");
            }

            if (json["retcode"]?.GetValue<int>() == 10001)
            {
                m_Logger.LogError("Invalid cookies for gameUid: {GameUid}", gameUid);
                return Result<GenshinAbyssInformation>.Failure(StatusCode.Unauthorized,
                    "Invalid HoYoLAB UID or Cookies. Please authenticate again.");
            }

            if (json["retcode"]?.GetValue<int>() != 0)
            {
                m_Logger.LogError("Failed to fetch Abyss information for gameUid: {GameUid}, retcode: {Retcode}",
                    gameUid, json["retcode"]);
                return Result<GenshinAbyssInformation>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later");
            }

            var abyssInfo = json["data"]?.Deserialize<GenshinAbyssInformation>()!;

            return Result<GenshinAbyssInformation>.Success(abyssInfo);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Failed to get Abyss information for gameUid: {GameUid}, region: {Region}",
                gameUid, region);
            return Result<GenshinAbyssInformation>.Failure(StatusCode.BotError,
                "An error occurred while fetching Abyss information");
        }
    }
}
