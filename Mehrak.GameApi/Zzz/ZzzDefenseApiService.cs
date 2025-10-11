using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Zzz.Types;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;

namespace Mehrak.GameApi.Zzz;

internal class ZzzDefenseApiService : IApiService<ZzzDefenseData>
{
    private const string ApiEndpoint = "/event/game_record_zzz/api/zzz/challenge";

    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<ZzzDefenseApiService> m_Logger;

    public ZzzDefenseApiService(IHttpClientFactory httpClientFactory, ILogger<ZzzDefenseApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<ZzzDefenseData>> GetAsync(ulong ltuid, string ltoken, string gameUid = "", string region = "")
    {
        if (string.IsNullOrEmpty(gameUid) || string.IsNullOrEmpty(region))
        {
            m_Logger.LogError("Game UID or region is null or empty");
            return Result<ZzzDefenseData>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        try
        {
            HttpClient client = m_HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new(HttpMethod.Get,
                $"{HoYoLabDomains.PublicApi}{ApiEndpoint}?server={region}&role_id={gameUid}&schedule_type=1");
            request.Headers.Add("Cookie", $"ltoken_v2={ltoken}; ltuid_v2={ltuid};");
            HttpResponseMessage response = await client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError("Failed to fetch Zzz Defense data for gameUid: {GameUid}, Status Code: {StatusCode}",
                    gameUid, response.StatusCode);
                return Result<ZzzDefenseData>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while fetching Shiyu Defense data");
            }

            ApiResponse<ZzzDefenseData>? json =
                await response.Content.ReadFromJsonAsync<ApiResponse<ZzzDefenseData>>();

            if (json == null)
            {
                m_Logger.LogError("Failed to fetch Zzz Defense data for gameUid: {GameUid}, Status Code: {StatusCode}",
                    gameUid, response.StatusCode);
                return Result<ZzzDefenseData>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while fetching Shiyu Defense data");
            }

            if (json.Retcode == 10001)
            {
                m_Logger.LogError("Invalid cookies for gameUid: {GameUid}", gameUid);
                return Result<ZzzDefenseData>.Failure(StatusCode.Unauthorized,
                    "Invalid cookies. Please re-authenticate.");
            }

            if (json.Retcode != 0)
            {
                m_Logger.LogWarning("Failed to fetch Zzz Defense data for {GameUid}, Retcode {Retcode}, Message: {Message}",
                    gameUid, json?.Retcode, json?.Message);
                return Result<ZzzDefenseData>.Failure(StatusCode.ExternalServerError,
                    $"Failed to fetch Zzz Defense data: {json!.Message} (Retcode: {json.Retcode})");
            }

            return Result<ZzzDefenseData>.Success(json.Data!);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "An error occurred while fetching Zzz Defense data for gameUid: {GameUid}", gameUid);
            return Result<ZzzDefenseData>.Failure(StatusCode.BotError,
                "An error occurred while fetching Shiyu Defense data");
        }
    }
}
