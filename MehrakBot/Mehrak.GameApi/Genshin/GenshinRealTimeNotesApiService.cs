#region

using System.Text.Json;
using System.Text.Json.Serialization;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Genshin.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.Genshin;

public class GenshinRealTimeNotesApiService : IApiService<GenshinRealTimeNotesData, BaseHoYoApiContext>
{
    private static readonly string ApiEndpoint = "/event/game_record/app/genshin/api/dailyNote";

    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<GenshinRealTimeNotesApiService> m_Logger;

    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public GenshinRealTimeNotesApiService(IHttpClientFactory httpClientFactory,
        ILogger<GenshinRealTimeNotesApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<GenshinRealTimeNotesData>> GetAsync(BaseHoYoApiContext context)
    {
        if (string.IsNullOrEmpty(context.GameUid) || string.IsNullOrEmpty(context.Region))
        {
            m_Logger.LogError(LogMessages.InvalidRegionOrUid);
            return Result<GenshinRealTimeNotesData>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        try
        {
            var requestUri =
                $"{HoYoLabDomains.PublicApi}{ApiEndpoint}?role_id={context.GameUid}&server={context.Region}";

            m_Logger.LogInformation(LogMessages.PreparingRequest, requestUri);

            var client = m_HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new(HttpMethod.Get, requestUri);
            request.Headers.Add("Cookie", $"ltuid_v2={context.LtUid}; ltoken_v2={context.LToken}");

            // Info-level outbound request (no headers)
            m_Logger.LogInformation(LogMessages.OutboundHttpRequest, request.Method, requestUri);
            var response = await client.SendAsync(request);

            // Info-level inbound response (status only)
            m_Logger.LogInformation(LogMessages.InboundHttpResponse, (int)response.StatusCode, requestUri);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError(LogMessages.NonSuccessStatusCode, response.StatusCode, requestUri);
                return Result<GenshinRealTimeNotesData>.Failure(StatusCode.ExternalServerError,
                    $"Failed to fetch real-time notes: {response.ReasonPhrase}", requestUri);
            }

            var json = await JsonSerializer.DeserializeAsync<ApiResponse<GenshinRealTimeNotesData>>(
                await response.Content.ReadAsStreamAsync(), JsonSerializerOptions);

            if (json?.Data == null)
            {
                m_Logger.LogError(LogMessages.EmptyResponseData, requestUri, context.UserId);
                return Result<GenshinRealTimeNotesData>.Failure(StatusCode.ExternalServerError,
                    "Failed to parse JSON response from real-time notes API", requestUri);
            }

            // Info-level API retcode after parse
            m_Logger.LogInformation(LogMessages.InboundHttpResponseWithRetcode, (int)response.StatusCode, requestUri, json.Retcode, context.UserId);

            if (json.Retcode == 10001)
            {
                m_Logger.LogError(LogMessages.InvalidCredentials, context.UserId);
                return Result<GenshinRealTimeNotesData>.Failure(StatusCode.Unauthorized,
                    "Invalid ltuid or ltoken provided for real-time notes API", requestUri);
            }

            if (json.Retcode != 0)
            {
                m_Logger.LogError(LogMessages.UnknownRetcode, json.Retcode, context.UserId, requestUri, json);
                return Result<GenshinRealTimeNotesData>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later", requestUri);
            }

            m_Logger.LogInformation(LogMessages.SuccessfullyRetrievedData, requestUri, context.UserId);
            return Result<GenshinRealTimeNotesData>.Success(json.Data, requestUri: requestUri);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, LogMessages.ExceptionOccurred,
                $"{HoYoLabDomains.PublicApi}{ApiEndpoint}", context.UserId);
            return Result<GenshinRealTimeNotesData>.Failure(StatusCode.BotError,
                "An error occurred while fetching real-time notes");
        }
    }
}
