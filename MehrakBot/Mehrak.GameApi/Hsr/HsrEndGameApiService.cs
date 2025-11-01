#region

using System.Text.Json;
using System.Text.Json.Serialization;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Hsr.Types;
using Mehrak.GameApi.Utilities;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.Hsr;

public class HsrEndGameApiService : IApiService<HsrEndInformation, HsrEndGameApiContext>
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<HsrEndGameApiService> m_Logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private static readonly string BasePath = "/event/game_record/hkrpg/api/";

    public HsrEndGameApiService(IHttpClientFactory httpClientFactory, ILogger<HsrEndGameApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<HsrEndInformation>> GetAsync(HsrEndGameApiContext context)
    {
        if (string.IsNullOrEmpty(context.GameUid) || string.IsNullOrEmpty(context.Region))
        {
            m_Logger.LogError(LogMessages.InvalidRegionOrUid);
            return Result<HsrEndInformation>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        try
        {
            var endpoint = context.GameMode switch
            {
                HsrEndGameMode.PureFiction => "challenge_story",
                HsrEndGameMode.ApocalypticShadow => "challenge_boss",
                _ => throw new NotImplementedException()
            };

            var requestUri =
                $"{HoYoLabDomains.PublicApi}{BasePath}{endpoint}?role_id={context.GameUid}&server={context.Region}&schedule_type=1&need_all=true";

            m_Logger.LogInformation(LogMessages.ReceivedRequest, requestUri);

            var client = m_HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new(HttpMethod.Get, requestUri);
            request.Headers.Add("Cookie", $"ltuid_v2={context.LtUid}; ltoken_v2={context.LToken}");
            request.Headers.Add("DS", DSGenerator.GenerateDS());
            request.Headers.Add("X-Rpc-App_version", "1.5.0");
            request.Headers.Add("X-Rpc-Language", "en-us");
            request.Headers.Add("X-Rpc-Client_type", "5");

            // Info-level outbound request (no headers)
            m_Logger.LogInformation(LogMessages.OutboundHttpRequest, request.Method, requestUri);
            var response = await client.SendAsync(request);

            // Info-level inbound response (status only)
            m_Logger.LogInformation(LogMessages.InboundHttpResponse, (int)response.StatusCode, requestUri);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError(LogMessages.NonSuccessStatusCode, response.StatusCode, requestUri);
                return Result<HsrEndInformation>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later", requestUri);
            }

            var json = await JsonSerializer.DeserializeAsync<ApiResponse<HsrEndInformation>>(
                await response.Content.ReadAsStreamAsync(), JsonOptions);

            if (json?.Data == null)
            {
                m_Logger.LogError(LogMessages.FailedToParseResponse, requestUri, context.GameUid);
                return Result<HsrEndInformation>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later", requestUri);
            }

            // Info-level API retcode after parse
            m_Logger.LogInformation(LogMessages.InboundHttpResponseWithRetcode, (int)response.StatusCode, requestUri, json.Retcode, context.GameUid);

            if (json.Retcode ==10001)
            {
                m_Logger.LogError(LogMessages.InvalidCredentials, context.GameUid);
                return Result<HsrEndInformation>.Failure(StatusCode.Unauthorized,
                    "Invalid HoYoLAB UID or Cookies. Please authenticate again.", requestUri);
            }

            if (json.Retcode !=0)
            {
                m_Logger.LogError(LogMessages.UnknownRetcode, json.Retcode, context.GameUid, requestUri);
                return Result<HsrEndInformation>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later", requestUri);
            }

            m_Logger.LogInformation(LogMessages.SuccessfullyRetrievedData, requestUri, context.GameUid);
            return Result<HsrEndInformation>.Success(json.Data, requestUri: requestUri);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, LogMessages.ExceptionOccurred,
                $"{HoYoLabDomains.PublicApi}{BasePath}{context.GameMode.GetString()}", context.GameUid);
            return Result<HsrEndInformation>.Failure(StatusCode.BotError,
                $"An unknown error occurred while fetching {context.GameMode.GetString()} information");
        }
    }
}
