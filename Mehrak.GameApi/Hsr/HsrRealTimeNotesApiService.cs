#region

using Mehrak.Domain.Interfaces;
using Mehrak.GameApi.Hsr.Types;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

#endregion

namespace Mehrak.GameApi.Hsr;

public class HsrRealTimeNotesApiService : IRealTimeNotesApiService<HsrRealTimeNotesData>
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<HsrRealTimeNotesApiService> m_Logger;

    private static readonly string ApiEndpoint = "/event/game_record/hkrpg/api/note";

    public HsrRealTimeNotesApiService(IHttpClientFactory httpClientFactory, ILogger<HsrRealTimeNotesApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<ApiResult<HsrRealTimeNotesData>> GetRealTimeNotesAsync(string roleId, string server, ulong ltuid,
        string ltoken)
    {
        try
        {
            var client = m_HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new(HttpMethod.Get, $"{HoYoLabDomains.PublicApi}{ApiEndpoint}?role_id={roleId}&server={server}");
            request.Headers.Add("Cookie", $"ltuid_v2={ltuid}; ltoken_v2={ltoken}");
            request.Headers.Add("X-Rpc-Client_type", "5");
            request.Headers.Add("X-Rpc-App_version", "1.5.0");
            request.Headers.Add("X-Rpc-Language", "en-us");
            request.Headers.Add("DS", DSGenerator.GenerateDS());

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError("Failed to fetch real-time notes: {StatusCode}", response.StatusCode);
                return ApiResult<HsrRealTimeNotesData>.Failure(HttpStatusCode.BadGateway,
                    $"Failed to fetch real-time notes: {response.ReasonPhrase}");
            }

            var json = await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync());
            if (json == null)
            {
                m_Logger.LogError("Failed to parse JSON response from real-time notes API");
                return ApiResult<HsrRealTimeNotesData>.Failure(HttpStatusCode.BadGateway,
                    "Failed to parse JSON response from real-time notes API");
            }

            if (json["retcode"]!.GetValue<int>() == 10001)
            {
                m_Logger.LogError("Invalid ltuid or ltoken provided for real-time notes API");
                return ApiResult<HsrRealTimeNotesData>.Failure(HttpStatusCode.Unauthorized,
                    "Invalid ltuid or ltoken provided for real-time notes API");
            }

            if (json["data"] == null)
            {
                m_Logger.LogError("No data found in real-time notes API response");
                return ApiResult<HsrRealTimeNotesData>.Failure(HttpStatusCode.BadGateway,
                    "No data found in real-time notes API response");
            }

            HsrRealTimeNotesData data = json["data"].Deserialize<HsrRealTimeNotesData>();
            if (data != null) return ApiResult<HsrRealTimeNotesData>.Success(data);

            m_Logger.LogError("Failed to deserialize real-time notes data");
            return ApiResult<HsrRealTimeNotesData>.Failure(HttpStatusCode.BadGateway,
                "Failed to deserialize real-time notes data");
        }
        catch (Exception e)
        {
            m_Logger.LogError(e,
                "An error occurred while fetching real-time notes for roleId {RoleId} on server {Server}",
                roleId, server);
            return ApiResult<HsrRealTimeNotesData>.Failure(HttpStatusCode.InternalServerError,
                "An error occurred while fetching real-time notes");
        }
    }
}
