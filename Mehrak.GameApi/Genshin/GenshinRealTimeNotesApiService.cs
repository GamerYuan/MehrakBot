#region

using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Genshin.Types;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;

#endregion

namespace Mehrak.GameApi.Genshin;

public class GenshinRealTimeNotesApiService : IApiService<GenshinRealTimeNotesData>
{
    private static readonly string ApiEndpoint = "/event/game_record/app/genshin/api/dailyNote";

    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<GenshinRealTimeNotesApiService> m_Logger;

    public GenshinRealTimeNotesApiService(IHttpClientFactory httpClientFactory,
        ILogger<GenshinRealTimeNotesApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<GenshinRealTimeNotesData>> GetAsync(ulong ltuid, string ltoken,
        string gameUid = "", string region = "")
    {
        if (string.IsNullOrEmpty(gameUid) || string.IsNullOrEmpty(region))
        {
            m_Logger.LogError("Game UID or region is null or empty");
            return Result<GenshinRealTimeNotesData>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        try
        {
            var client = m_HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new(HttpMethod.Get, $"{HoYoLabDomains.PublicApi}{ApiEndpoint}?role_id={gameUid}&server={region}");
            request.Headers.Add("Cookie", $"ltuid_v2={ltuid}; ltoken_v2={ltoken}");
            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError("Failed to fetch real-time notes: {StatusCode}", response.StatusCode);
                return Result<GenshinRealTimeNotesData>.Failure(StatusCode.ExternalServerError,
                    $"Failed to fetch real-time notes: {response.ReasonPhrase}");
            }

            var json = await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync());
            if (json == null)
            {
                m_Logger.LogError("Failed to parse JSON response from real-time notes API");
                return Result<GenshinRealTimeNotesData>.Failure(StatusCode.ExternalServerError,
                    "Failed to parse JSON response from real-time notes API");
            }

            if (json["retcode"]!.GetValue<int>() == 10001)
            {
                m_Logger.LogError("Invalid ltuid or ltoken provided for real-time notes API");
                return Result<GenshinRealTimeNotesData>.Failure(StatusCode.Unauthorized,
                    "Invalid ltuid or ltoken provided for real-time notes API");
            }

            if (json["data"] == null)
            {
                m_Logger.LogError("No data found in real-time notes API response");
                return Result<GenshinRealTimeNotesData>.Failure(StatusCode.ExternalServerError,
                    "No data found in real-time notes API response");
            }

            GenshinRealTimeNotesData? data = json["data"]!.Deserialize<GenshinRealTimeNotesData>();
            if (data != null) return Result<GenshinRealTimeNotesData>.Success(data);

            m_Logger.LogError("Failed to deserialize real-time notes data for roleId {RoleId} on server {Server}",
                gameUid, region);
            return Result<GenshinRealTimeNotesData>.Failure(StatusCode.ExternalServerError,
                "Failed to deserialize real-time notes data");
        }
        catch (Exception e)
        {
            m_Logger.LogError(e,
                "An error occurred while fetching real-time notes for roleId {RoleId} on server {Server}",
                gameUid, region);
            return Result<GenshinRealTimeNotesData>.Failure(StatusCode.BotError,
                "An error occurred while fetching real-time notes");
        }
    }
}
