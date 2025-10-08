using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Zzz.Types;
using System.Net.Http.Json;

namespace Mehrak.GameApi.Zzz;

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
            m_Logger.LogError("Failed to fetch Zzz Defense data for gameUid: {GameUid}, Status Code: {StatusCode}",
                gameUid, response.StatusCode);
            throw new CommandException("An unknown error occurred when accessing HoYoLAB API. Please try again later");
        }

        ApiResponse<ZzzDefenseData>? json =
            await response.Content.ReadFromJsonAsync<ApiResponse<ZzzDefenseData>>();

        if (json == null)
        {
            m_Logger.LogError("Failed to fetch Zzz Defense data for gameUid: {GameUid}, Status Code: {StatusCode}",
                gameUid, response.StatusCode);
            throw new CommandException("An unknown error occurred when accessing HoYoLAB API. Please try again later");
        }

        if (json.Retcode == 10001)
        {
            m_Logger.LogError("Invalid cookies for gameUid: {GameUid}", gameUid);
            throw new CommandException("Invalid HoYoLAB UID or Cookies. Please authenticate again.");
        }

        if (json.Retcode != 0)
        {
            m_Logger.LogWarning("Failed to fetch Zzz Defense data for {GameUid}, Retcode {Retcode}, Message: {Message}",
                gameUid, json?.Retcode, json?.Message);
            throw new CommandException("An error occurred while fetching Shiyu Defense data");
        }

        return json.Data!;
    }
}
