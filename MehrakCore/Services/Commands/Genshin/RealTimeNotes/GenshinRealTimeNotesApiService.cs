#region

using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.Json.Nodes;
using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Models;
using Microsoft.Extensions.Logging;

#endregion

namespace MehrakCore.Services.Commands.Genshin.RealTimeNotes;

public class GenshinRealTimeNotesApiService : IRealTimeNotesApiService<GenshinRealTimeNotesData>
{
    private const string ApiUrl = "https://sg-public-api.hoyolab.com/event/game_record/app/genshin/api/dailyNote";

    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<GenshinRealTimeNotesApiService> m_Logger;

    public GenshinRealTimeNotesApiService(IHttpClientFactory httpClientFactory,
        ILogger<GenshinRealTimeNotesApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<ApiResult<GenshinRealTimeNotesData>> GetRealTimeNotesAsync(string roleId, string server,
        ulong ltuid, string ltoken)
    {
        try
        {
            var client = m_HttpClientFactory.CreateClient("Default");
            var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiUrl}?role_id={roleId}&server={server}");
            request.Headers.Add("Cookie", $"ltuid_v2={ltuid}; ltoken_v2={ltoken}");
            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError("Failed to fetch real-time notes: {StatusCode}", response.StatusCode);
                return ApiResult<GenshinRealTimeNotesData>.Failure(response.StatusCode,
                    $"Failed to fetch real-time notes: {response.ReasonPhrase}");
            }

            var json = await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync());
            if (json == null)
            {
                m_Logger.LogError("Failed to parse JSON response from real-time notes API");
                return ApiResult<GenshinRealTimeNotesData>.Failure(HttpStatusCode.BadGateway,
                    "Failed to parse JSON response from real-time notes API");
            }

            if (json["retcode"]!.GetValue<int>() == 10001)
            {
                m_Logger.LogError("Invalid ltuid or ltoken provided for real-time notes API");
                return ApiResult<GenshinRealTimeNotesData>.Failure(HttpStatusCode.Unauthorized,
                    "Invalid ltuid or ltoken provided for real-time notes API");
            }

            if (json["data"] == null)
            {
                m_Logger.LogError("No data found in real-time notes API response");
                return ApiResult<GenshinRealTimeNotesData>.Failure(HttpStatusCode.BadGateway,
                    "No data found in real-time notes API response");
            }

            var data = json["data"]!.Deserialize<GenshinRealTimeNotesData>();
            if (data != null) return ApiResult<GenshinRealTimeNotesData>.Success(data);

            m_Logger.LogError("Failed to deserialize real-time notes data for roleId {RoleId} on server {Server}",
                roleId, server);
            return ApiResult<GenshinRealTimeNotesData>.Failure(HttpStatusCode.BadGateway,
                "Failed to deserialize real-time notes data");
        }
        catch (Exception e)
        {
            m_Logger.LogError(e,
                "An error occurred while fetching real-time notes for roleId {RoleId} on server {Server}",
                roleId, server);
            return ApiResult<GenshinRealTimeNotesData>.Failure(HttpStatusCode.InternalServerError,
                "An error occurred while fetching real-time notes");
        }
    }

    public async Task<IEnumerable<(string, bool)>> GetApiStatusAsync()
    {
        Ping ping = new();
        var result = await ping.SendPingAsync(new Uri(ApiUrl).Host);
        return [("Genshin Impact", result.Status == IPStatus.Success)];
    }
}
