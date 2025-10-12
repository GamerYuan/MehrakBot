#region

using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Hsr.Types;
using Mehrak.GameApi.Utilities;
using Microsoft.Extensions.Logging;
using System.Text.Json;

#endregion

namespace Mehrak.GameApi.Hsr;

public class HsrRealTimeNotesApiService : IApiService<HsrRealTimeNotesData, BaseHoYoApiContext>
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<HsrRealTimeNotesApiService> m_Logger;

    private static readonly string ApiEndpoint = "/event/game_record/hkrpg/api/note";

    public HsrRealTimeNotesApiService(IHttpClientFactory httpClientFactory, ILogger<HsrRealTimeNotesApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<HsrRealTimeNotesData>> GetAsync(BaseHoYoApiContext context)
    {
        if (string.IsNullOrEmpty(context.GameUid) || string.IsNullOrEmpty(context.Region))
        {
            m_Logger.LogError("Game UID or region is null or empty");
            return Result<HsrRealTimeNotesData>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        try
        {
            var client = m_HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new(HttpMethod.Get, $"{HoYoLabDomains.PublicApi}{ApiEndpoint}?role_id={context.GameUid}&server={context.Region}");
            request.Headers.Add("Cookie", $"ltuid_v2={context.LtUid}; ltoken_v2={context.LToken}");
            request.Headers.Add("X-Rpc-Client_type", "5");
            request.Headers.Add("X-Rpc-App_version", "1.5.0");
            request.Headers.Add("X-Rpc-Language", "en-us");
            request.Headers.Add("DS", DSGenerator.GenerateDS());

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError("Failed to fetch real-time notes: {StatusCode}", response.StatusCode);
                return Result<HsrRealTimeNotesData>.Failure(StatusCode.ExternalServerError,
                    $"Failed to fetch real-time notes: {response.ReasonPhrase}");
            }

            var json = await JsonSerializer.DeserializeAsync<ApiResponse<HsrRealTimeNotesData>>(await response.Content.ReadAsStreamAsync());
            if (json?.Data == null)
            {
                m_Logger.LogError("Failed to parse JSON response from real-time notes API");
                return Result<HsrRealTimeNotesData>.Failure(StatusCode.ExternalServerError,
                    "Failed to parse JSON response from real-time notes API");
            }

            if (json.Retcode == 10001)
            {
                m_Logger.LogError("Invalid ltuid or ltoken provided for real-time notes API");
                return Result<HsrRealTimeNotesData>.Failure(StatusCode.Unauthorized,
                    "Invalid ltuid or ltoken provided for real-time notes API");
            }

            return Result<HsrRealTimeNotesData>.Success(json.Data);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e,
                "An error occurred while fetching real-time notes for roleId {RoleId} on server {Server}",
                context.GameUid, context.Region);
            return Result<HsrRealTimeNotesData>.Failure(StatusCode.BotError,
                "An error occurred while fetching real-time notes");
        }
    }
}
