using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common;
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
        if (string.IsNullOrEmpty(context.GameUid) || string.IsNullOrEmpty(context.Region))
        {
            m_Logger.LogError(LogMessages.InvalidRegionOrUid);
            return Result<ZzzAssaultData>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        try
        {
            var requestUri =
                $"{HoYoLabDomains.PublicApi}{ApiEndpoint}?region={context.Region}&uid={context.GameUid}&schedule_type=1";

            m_Logger.LogInformation(LogMessages.ReceivedRequest, requestUri);

            HttpClient client = m_HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new(HttpMethod.Get, requestUri);
            request.Headers.Add("Cookie", $"ltoken_v2={context.LToken}; ltuid_v2={context.LtUid};");

            m_Logger.LogDebug(LogMessages.SendingRequest, requestUri);
            HttpResponseMessage response = await client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError(LogMessages.NonSuccessStatusCode, response.StatusCode, requestUri);
                return Result<ZzzAssaultData>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later");
            }

            ApiResponse<ZzzAssaultData>? json =
                await response.Content.ReadFromJsonAsync<ApiResponse<ZzzAssaultData>>();

            if (json?.Data == null)
            {
                m_Logger.LogError(LogMessages.FailedToParseResponse, requestUri, context.GameUid);
                return Result<ZzzAssaultData>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later");
            }

            if (json.Retcode == 10001)
            {
                m_Logger.LogError(LogMessages.InvalidCredentials, context.GameUid);
                return Result<ZzzAssaultData>.Failure(StatusCode.Unauthorized,
                    "Invalid HoYoLAB UID or Cookies. Please authenticate again");
            }

            if (json.Retcode != 0)
            {
                m_Logger.LogError(LogMessages.UnknownRetcode, json.Retcode, context.GameUid, requestUri);
                return Result<ZzzAssaultData>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later");
            }

            m_Logger.LogInformation(LogMessages.SuccessfullyRetrievedData, requestUri, context.GameUid);
            return Result<ZzzAssaultData>.Success(json.Data);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, LogMessages.ExceptionOccurred,
                $"{HoYoLabDomains.PublicApi}{ApiEndpoint}", context.GameUid);
            return Result<ZzzAssaultData>.Failure(StatusCode.BotError,
                "An error occurred while retrieving assault data");
        }
    }
}
