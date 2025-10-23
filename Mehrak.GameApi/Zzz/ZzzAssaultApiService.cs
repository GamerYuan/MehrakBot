using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Zzz.Types;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;

namespace Mehrak.GameApi.Zzz;

public class ZzzAssaultApiService : IApiService<ZzzAssaultData, BaseHoYoApiContext>
{
    private const string ApiEndpoint = "/event/game_record_zzz/api/zzz/mem_detail";

    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<ZzzAssaultApiService> m_Logger;

    public ZzzAssaultApiService(IHttpClientFactory clientFactory, ILogger<ZzzAssaultApiService> logger)
    {
        m_HttpClientFactory = clientFactory;
        m_Logger = logger;
    }

    public async Task<Result<ZzzAssaultData>> GetAsync(BaseHoYoApiContext context)
    {
        try
        {
            if (string.IsNullOrEmpty(context.GameUid) || string.IsNullOrEmpty(context.Region))
            {
                m_Logger.LogError("Game UID or region is null or empty");
                return Result<ZzzAssaultData>.Failure(StatusCode.BadParameter,
                    "Game UID or region is null or empty");
            }

            HttpClient client = m_HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new(HttpMethod.Get,
                $"{HoYoLabDomains.PublicApi}{ApiEndpoint}?region={context.Region}&uid={context.GameUid}&schedule_type=1");
            request.Headers.Add("Cookie", $"ltoken_v2={context.LToken}; ltuid_v2={context.LtUid};");
            HttpResponseMessage response = await client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError("Failed to fetch Zzz Assault data for gameUid: {GameUid}, Status Code: {StatusCode}",
                    context.GameUid, response.StatusCode);
                return Result<ZzzAssaultData>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later");
            }

            ApiResponse<ZzzAssaultData>? json =
                await response.Content.ReadFromJsonAsync<ApiResponse<ZzzAssaultData>>();

            if (json?.Data == null)
            {
                m_Logger.LogError("Failed to fetch Zzz Assault data for gameUid: {GameUid}, Status Code: {StatusCode}",
                    context.GameUid, response.StatusCode);
                return Result<ZzzAssaultData>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later");
            }

            if (json.Retcode == 10001)
            {
                m_Logger.LogError("Invalid cookies for gameUid: {GameUid}", context.GameUid);
                return Result<ZzzAssaultData>.Failure(StatusCode.Unauthorized, "Invalid HoYoLAB UID or Cookies. Please authenticate again");
            }

            if (json.Retcode != 0)
            {
                m_Logger.LogWarning("Failed to fetch Zzz Assault data for {GameUid}, Retcode {Retcode}, Message: {Message}",
                    context.GameUid, json?.Retcode, json?.Message);
                return Result<ZzzAssaultData>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later");
            }

            return Result<ZzzAssaultData>.Success(json.Data);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e,
                "Failed to retrieve assault data for user {Uid} on {Region} server (game UID: {GameUid})",
                context.UserId, context.Region, context.GameUid);
            return Result<ZzzAssaultData>.Failure(StatusCode.BotError,
                "An error occurred while retrieving assault data");
        }
    }
}
