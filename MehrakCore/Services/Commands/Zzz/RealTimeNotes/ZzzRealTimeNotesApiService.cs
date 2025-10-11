using MehrakCore.ApiResponseTypes;
using MehrakCore.ApiResponseTypes.Zzz;
using MehrakCore.Constants;
using MehrakCore.Models;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MehrakCore.Services.Commands.Zzz.RealTimeNotes;

internal class ZzzRealTimeNotesApiService : IRealTimeNotesApiService<ZzzRealTimeNotesData>
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<ZzzRealTimeNotesApiService> m_Logger;

    private const string ApiEndpoint = "/event/game_record_zzz/api/zzz/note";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public ZzzRealTimeNotesApiService(IHttpClientFactory httpClientFactory,
        ILogger<ZzzRealTimeNotesApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<ZzzRealTimeNotesData>> GetRealTimeNotesAsync(
        string roleId, string server, ulong ltuid, string ltoken)
    {
        try
        {
            HttpClient client = m_HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new(HttpMethod.Get, $"{HoYoLabDomains.PublicApi}{ApiEndpoint}?role_id={roleId}&server={server}");
            request.Headers.Add("Cookie", $"ltuid_v2={ltuid}; ltoken_v2={ltoken}");
            HttpResponseMessage response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError("Failed to fetch real-time notes: {StatusCode}", response.StatusCode);
                return Result<ZzzRealTimeNotesData>.Failure(response.StatusCode,
                    $"Failed to fetch real-time notes: {response.ReasonPhrase}");
            }

            ApiResponse<ZzzRealTimeNotesData>? json = await
                JsonSerializer.DeserializeAsync<ApiResponse<ZzzRealTimeNotesData>>(await response.Content.ReadAsStreamAsync(), JsonOptions);
            if (json == null)
            {
                m_Logger.LogError("Failed to parse JSON response from real-time notes API");
                return Result<ZzzRealTimeNotesData>.Failure(HttpStatusCode.BadGateway,
                    "Failed to parse JSON response from real-time notes API");
            }

            if (json.Retcode == 10001)
            {
                m_Logger.LogError("Invalid ltuid or ltoken provided for real-time notes API");
                return Result<ZzzRealTimeNotesData>.Failure(HttpStatusCode.Unauthorized,
                    "Invalid ltuid or ltoken provided for real-time notes API");
            }

            if (json.Data == null)
            {
                m_Logger.LogError("No data found in real-time notes API response");
                return Result<ZzzRealTimeNotesData>.Failure(HttpStatusCode.BadGateway,
                    "No data found in real-time notes API response");
            }

            return Result<ZzzRealTimeNotesData>.Success(json.Data);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e,
                "An error occurred while fetching real-time notes for roleId {RoleId} on server {Server}",
                roleId, server);
            return Result<ZzzRealTimeNotesData>.Failure(HttpStatusCode.InternalServerError,
                "An error occurred while fetching real-time notes");
        }
    }
}
