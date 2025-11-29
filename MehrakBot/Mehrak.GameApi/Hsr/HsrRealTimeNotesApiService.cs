#region

using System.Text.Json;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Hsr.Types;
using Mehrak.GameApi.Utilities;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.Hsr;

public class HsrRealTimeNotesApiService : IApiService<HsrRealTimeNotesData, BaseHoYoApiContext>
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<HsrRealTimeNotesApiService> m_Logger;

    private static readonly string ApiEndpoint = "/event/game_record/hkrpg/api/note";

    public HsrRealTimeNotesApiService(IHttpClientFactory httpClientFactory, ILogger<HsrRealTimeNotesApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<HsrRealTimeNotesData>> GetAsync(BaseHoYoApiContext context)
    {
        if (string.IsNullOrEmpty(context.GameUid) || string.IsNullOrEmpty(context.Region))
        {
            m_Logger.LogError(LogMessages.InvalidRegionOrUid);
            return Result<HsrRealTimeNotesData>.Failure(StatusCode.BadParameter,
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
            request.Headers.Add("X-Rpc-Client_type", "5");
            request.Headers.Add("X-Rpc-App_version", "1.5.0");
            request.Headers.Add("X-Rpc-Language", "en-us");
            request.Headers.Add("DS", DSGenerator.GenerateDS());

            // Info-level outbound request (no headers)
            m_Logger.LogInformation(LogMessages.OutboundHttpRequest, request.Method, requestUri);
            var response = await client.SendAsync(request);

            // Info-level inbound response (status only)
            m_Logger.LogInformation(LogMessages.InboundHttpResponse, (int)response.StatusCode, requestUri);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError(LogMessages.NonSuccessStatusCode, response.StatusCode, requestUri);
                return Result<HsrRealTimeNotesData>.Failure(StatusCode.ExternalServerError,
                    $"Failed to fetch real-time notes: {response.ReasonPhrase}", requestUri);
            }

            var json = await JsonSerializer.DeserializeAsync<ApiResponse<HsrRealTimeNotesData>>(
                await response.Content.ReadAsStreamAsync());

            if (json?.Data == null)
            {
                m_Logger.LogError(LogMessages.EmptyResponseData, requestUri, context.UserId);
                return Result<HsrRealTimeNotesData>.Failure(StatusCode.ExternalServerError,
                    "Failed to parse JSON response from real-time notes API", requestUri);
            }

            // Info-level API retcode after parse
            m_Logger.LogInformation(LogMessages.InboundHttpResponseWithRetcode, (int)response.StatusCode, requestUri, json.Retcode, context.UserId);

            if (json.Retcode == 10001)
            {
                m_Logger.LogError(LogMessages.InvalidCredentials, context.UserId);
                return Result<HsrRealTimeNotesData>.Failure(StatusCode.Unauthorized,
                    "Invalid ltuid or ltoken provided for real-time notes API", requestUri);
            }

            if (json.Retcode != 0)
            {
                m_Logger.LogError(LogMessages.UnknownRetcode, json.Retcode, context.UserId, requestUri);
                return Result<HsrRealTimeNotesData>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later", requestUri);
            }

            m_Logger.LogInformation(LogMessages.SuccessfullyRetrievedData, requestUri, context.UserId);
            return Result<HsrRealTimeNotesData>.Success(json.Data, requestUri: requestUri);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, LogMessages.ExceptionOccurred,
                $"{HoYoLabDomains.PublicApi}{ApiEndpoint}", context.UserId);
            return Result<HsrRealTimeNotesData>.Failure(StatusCode.BotError,
                "An error occurred while fetching real-time notes");
        }
    }
}
