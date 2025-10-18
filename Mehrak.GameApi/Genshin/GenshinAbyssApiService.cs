#region

using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Genshin.Types;
using Microsoft.Extensions.Logging;
using System.Text.Json;

#endregion

namespace Mehrak.GameApi.Genshin;

internal class GenshinAbyssApiService : IApiService<GenshinAbyssInformation, BaseHoYoApiContext>
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<GenshinAbyssApiService> m_Logger;
    private static readonly string ApiEndpoint = "/event/game_record/genshin/api/spiralAbyss";

    public GenshinAbyssApiService(IHttpClientFactory httpClientFactory, ILogger<GenshinAbyssApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<GenshinAbyssInformation>> GetAsync(BaseHoYoApiContext context)
    {
        if (string.IsNullOrEmpty(context.GameUid) || string.IsNullOrEmpty(context.Region))
        {
            m_Logger.LogError("Game UID or region is null or empty");
            return Result<GenshinAbyssInformation>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        try
        {
            var client = m_HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new(HttpMethod.Get,
                $"{HoYoLabDomains.PublicApi}{ApiEndpoint}?role_id={context.GameUid}&server={context.Region}&schedule_type=1");
            request.Headers.Add("Cookie", $"ltuid_v2={context.LtUid}; ltoken_v2={context.LToken}");
            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError("Failed to fetch Abyss information for gameUid: {GameUid}", context.GameUid);
                return Result<GenshinAbyssInformation>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later");
            }

            m_Logger.LogDebug("Retrieved json response {Response}", await response.Content.ReadAsStringAsync());
            var json = await JsonSerializer.DeserializeAsync<ApiResponse<GenshinAbyssInformation>>(await response.Content.ReadAsStreamAsync());
            if (json?.Data == null)
            {
                m_Logger.LogError("Failed to fetch Abyss information for gameUid: {GameUid}", context.GameUid);
                return Result<GenshinAbyssInformation>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later");
            }

            if (json.Retcode == 10001)
            {
                m_Logger.LogError("Invalid cookies for gameUid: {GameUid}", context.GameUid);
                return Result<GenshinAbyssInformation>.Failure(StatusCode.Unauthorized,
                    "Invalid HoYoLAB UID or Cookies. Please authenticate again.");
            }

            if (json.Retcode != 0)
            {
                m_Logger.LogError("Failed to fetch Abyss information for gameUid: {GameUid}, retcode: {Retcode}",
                    context.GameUid, json.Retcode);
                return Result<GenshinAbyssInformation>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later");
            }

            return Result<GenshinAbyssInformation>.Success(json.Data);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Failed to get Abyss information for gameUid: {GameUid}, region: {Region}",
                context.GameUid, context.Region);
            return Result<GenshinAbyssInformation>.Failure(StatusCode.BotError,
                "An error occurred while fetching Abyss information");
        }
    }
}
