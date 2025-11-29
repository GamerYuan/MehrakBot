#region

using System.Text.Json;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Genshin.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.Genshin;

internal class GenshinTheaterApiService : IApiService<GenshinTheaterInformation, BaseHoYoApiContext>
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<GenshinTheaterApiService> m_Logger;

    private static readonly string ApiEndpoint = "/event/game_record/genshin/api/role_combat";

    public GenshinTheaterApiService(IHttpClientFactory httpClientFactory, ILogger<GenshinTheaterApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<GenshinTheaterInformation>> GetAsync(BaseHoYoApiContext context)
    {
        if (string.IsNullOrEmpty(context.GameUid) || string.IsNullOrEmpty(context.Region))
        {
            m_Logger.LogError(LogMessages.InvalidRegionOrUid);
            return Result<GenshinTheaterInformation>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        try
        {
            var requestUri =
                $"{HoYoLabDomains.PublicApi}{ApiEndpoint}?role_id={context.GameUid}&server={context.Region}&need_detail=true";

            m_Logger.LogInformation(LogMessages.PreparingRequest, requestUri);

            HttpClient client = m_HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new(HttpMethod.Get, requestUri);
            request.Headers.Add("Cookie", $"ltuid_v2={context.LtUid}; ltoken_v2={context.LToken}");

            // Info-level outbound request (no headers)
            m_Logger.LogInformation(LogMessages.OutboundHttpRequest, request.Method, requestUri);
            HttpResponseMessage response = await client.SendAsync(request);

            // Info-level inbound response (status only)
            m_Logger.LogInformation(LogMessages.InboundHttpResponse, (int)response.StatusCode, requestUri);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError(LogMessages.NonSuccessStatusCode, response.StatusCode, requestUri);
                return Result<GenshinTheaterInformation>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while retrieving Imaginarium Theater data", requestUri);
            }

            ApiResponse<GenshinTheaterResponseData>? json = await JsonSerializer.DeserializeAsync<ApiResponse<GenshinTheaterResponseData>>(
                await response.Content.ReadAsStreamAsync());

            if (json?.Data == null)
            {
                m_Logger.LogError(LogMessages.EmptyResponseData, requestUri, context.UserId);
                return Result<GenshinTheaterInformation>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while retrieving Imaginarium Theater data", requestUri);
            }

            // Info-level API retcode after parse
            m_Logger.LogInformation(LogMessages.InboundHttpResponseWithRetcode, (int)response.StatusCode, requestUri, json.Retcode, context.UserId);

            if (json.Retcode == 10001)
            {
                m_Logger.LogError(LogMessages.InvalidCredentials, context.UserId);
                return Result<GenshinTheaterInformation>.Failure(StatusCode.Unauthorized,
                    "Invalid HoYoLAB UID or Cookies. Please authenticate again", requestUri);
            }

            if (json.Retcode != 0)
            {
                m_Logger.LogError(LogMessages.UnknownRetcode, json.Retcode, context.UserId, requestUri);
                return Result<GenshinTheaterInformation>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while retrieving Imaginarium Theater data", requestUri);
            }

            if (!json.Data.IsUnlock)
            {
                m_Logger.LogInformation(LogMessages.FeatureNotUnlocked, "Imaginarium Theater", context.UserId);
                return Result<GenshinTheaterInformation>.Failure(StatusCode.Unauthorized,
                    "Imaginarium Theater is not unlocked yet", requestUri);
            }

            List<GenshinTheaterInformation> theaterInfo = json.Data.Data;
            if (theaterInfo == null || theaterInfo.Count == 0)
            {
                m_Logger.LogError(LogMessages.DataNotFoundForFeature, "Imaginarium Theater", context.UserId);
                return Result<GenshinTheaterInformation>.Failure(StatusCode.ExternalServerError,
                    "No Imaginarium Theater data found", requestUri);
            }

            m_Logger.LogInformation(LogMessages.SuccessfullyRetrievedData, requestUri, context.UserId);
            return Result<GenshinTheaterInformation>.Success(json.Data.Data[0], requestUri: requestUri);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, LogMessages.ExceptionOccurred,
                $"{HoYoLabDomains.PublicApi}{ApiEndpoint}", context.UserId);
            return Result<GenshinTheaterInformation>.Failure(StatusCode.BotError,
                "An error occurred while retrieving Imaginarium Theater data");
        }
    }
}
