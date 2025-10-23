#region

using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Genshin.Types;
using Microsoft.Extensions.Logging;
using System.Text.Json;

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

            m_Logger.LogInformation(LogMessages.ReceivedRequest, requestUri);

            var client = m_HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new(HttpMethod.Get, requestUri);
            request.Headers.Add("Cookie", $"ltuid_v2={context.LtUid}; ltoken_v2={context.LToken}");

            m_Logger.LogDebug(LogMessages.SendingRequest, requestUri);
            var response = await client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError(LogMessages.NonSuccessStatusCode, response.StatusCode, requestUri);
                return Result<GenshinTheaterInformation>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while retrieving Imaginarium Theater data");
            }

            var json = await JsonSerializer.DeserializeAsync<ApiResponse<GenshinTheaterResponseData>>(
                await response.Content.ReadAsStreamAsync());

            if (json?.Data == null)
            {
                m_Logger.LogError(LogMessages.FailedToParseResponse, requestUri, context.GameUid);
                return Result<GenshinTheaterInformation>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while retrieving Imaginarium Theater data");
            }

            if (json.Retcode == 10001)
            {
                m_Logger.LogError(LogMessages.InvalidCredentials, context.GameUid);
                return Result<GenshinTheaterInformation>.Failure(StatusCode.Unauthorized,
                    "Invalid HoYoLAB UID or Cookies. Please authenticate again");
            }

            if (json.Retcode != 0)
            {
                m_Logger.LogError(LogMessages.UnknownRetcode, json.Retcode, context.GameUid, requestUri);
                return Result<GenshinTheaterInformation>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while retrieving Imaginarium Theater data");
            }

            if (!json.Data.IsUnlock)
            {
                m_Logger.LogInformation("Imaginarium Theater is not unlocked for gameUid: {GameUid}", context.GameUid);
                return Result<GenshinTheaterInformation>.Failure(StatusCode.Unauthorized,
                    "Imaginarium Theater is not unlocked yet");
            }

            var theaterInfo = json.Data.Data;
            if (theaterInfo == null || theaterInfo.Count == 0)
            {
                m_Logger.LogError("No Theater data found for gameUid: {GameUid}", context.GameUid);
                return Result<GenshinTheaterInformation>.Failure(StatusCode.ExternalServerError,
                    "No Imaginarium Theater data found");
            }

            m_Logger.LogInformation(LogMessages.SuccessfullyRetrievedData, requestUri, context.GameUid);
            return Result<GenshinTheaterInformation>.Success(json.Data.Data[0]);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, LogMessages.ExceptionOccurred,
                $"{HoYoLabDomains.PublicApi}{ApiEndpoint}", context.GameUid);
            return Result<GenshinTheaterInformation>.Failure(StatusCode.BotError,
                "An error occurred while retrieving Imaginarium Theater data");
        }
    }
}
