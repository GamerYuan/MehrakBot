using MehrakCore.ApiResponseTypes;
using MehrakCore.ApiResponseTypes.Zzz;
using MehrakCore.Constants;
using MehrakCore.Models;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;

namespace MehrakCore.Services.Commands.Zzz.Defense;

internal class ZzzDefenseApiService : IApiService<ZzzDefenseCommandExecutor>
{
    private const string ApiEndpoint = "/event/game_record_zzz/api/zzz/challenge";

    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<ZzzDefenseApiService> m_Logger;

    public ZzzDefenseApiService(IHttpClientFactory httpClientFactory, ILogger<ZzzDefenseApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async ValueTask<ZzzDefenseData> GetDefenseDataAsync(string ltoken, ulong ltuid, string gameUid, string region)
    {
        HttpClient client = m_HttpClientFactory.CreateClient("Default");
        HttpRequestMessage request = new(HttpMethod.Get,
            $"{HoYoLabDomains.PublicApi}{ApiEndpoint}?server={region}&role_id={gameUid}&schedule_type=1");
        request.Headers.Add("Cookie", $"ltoken_v2={ltoken}; ltuid_v2={ltuid};");
        HttpResponseMessage response = await client.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            m_Logger.LogError("Failed to fetch Zzz Defense data. Status Code: {StatusCode}", response.StatusCode);
            throw new HttpRequestException($"Failed to fetch Zzz Defense data. Status Code: {response.StatusCode}");
        }

        ApiResponse<ZzzDefenseData>? json =
            await response.Content.ReadFromJsonAsync<ApiResponse<ZzzDefenseData>>();

        if (json == null || json.Retcode != 0)
        {
            m_Logger.LogError("Error in API response: Retcode {Retcode}, Message: {Message}",
                json?.Retcode, json?.Message);
            throw new CommandException("An error occurred while fetching Shiyu Defense data");
        }

        return json.Data!;
    }
}
