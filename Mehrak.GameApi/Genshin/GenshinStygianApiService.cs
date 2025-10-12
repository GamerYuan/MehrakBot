#region

using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Genshin.Types;
using Microsoft.Extensions.Logging;
using System.Text.Json;

#endregion

namespace Mehrak.GameApi.Genshin;

internal class GenshinStygianApiService : IApiService<GenshinStygianInformation, BaseHoYoApiContext>
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<GenshinStygianApiService> m_Logger;

    private static readonly string ApiEndpoint = "/event/game_record/genshin/api/hard_challenge";

    public GenshinStygianApiService(IHttpClientFactory httpClientFactory, ILogger<GenshinStygianApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<GenshinStygianInformation>> GetAsync(BaseHoYoApiContext context)
    {
        if (string.IsNullOrEmpty(context.GameUid) || string.IsNullOrEmpty(context.Region))
        {
            m_Logger.LogError("Game UID or region is null or empty");
            return Result<GenshinStygianInformation>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        try
        {
            var client = m_HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new(HttpMethod.Get,
                $"{HoYoLabDomains.PublicApi}{ApiEndpoint}?role_id={context.GameUid}&server={context.Region}&need_detail=true");
            request.Headers.Add("Cookie", $"ltuid_v2={context.LtUid}; ltoken_v2={context.LToken}");
            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError("Failed to fetch Stygian data for gameUid: {GameUid}", context.GameUid);
                return Result<GenshinStygianInformation>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while retrieving Stygian Onslaught data");
            }

            var json = await JsonSerializer.DeserializeAsync<ApiResponse<GenshinStygianInformation>>(await response.Content.ReadAsStreamAsync());
            if (json?.Data == null)
            {
                m_Logger.LogError("Failed to parse Stygian data for gameUid: {GameUid}", context.GameUid);
                return Result<GenshinStygianInformation>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while retrieving Stygian Onslaught data");
            }

            if (json.Retcode == 10001)
            {
                m_Logger.LogError("Invalid cookies for gameUid: {GameUid}", context.GameUid);
                return Result<GenshinStygianInformation>.Failure(StatusCode.Unauthorized,
                    "Invalid HoYoLAB UID or Cookies. Please authenticate again");
            }

            if (json.Retcode != 0)
            {
                m_Logger.LogError("Failed to fetch Stygian data for gameUid: {GameUid}, retcode: {Retcode}",
                    context.GameUid, json.Retcode);
                return Result<GenshinStygianInformation>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while retrieving Stygian Onslaught data");
            }

            return Result<GenshinStygianInformation>.Success(json.Data);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "An error occurred while fetching Stygian data for gameUid: {GameUid}", context.GameUid);
            return Result<GenshinStygianInformation>.Failure(StatusCode.ExternalServerError,
                "An error occurred while retrieving Stygian Onslaught data");
        }
    }
}
