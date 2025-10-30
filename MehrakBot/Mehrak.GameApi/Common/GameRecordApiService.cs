#region

using System.Net.Http.Json;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.Common;

public class GameRecordApiService : IApiService<IEnumerable<GameRecordDto>, GameRecordApiContext>
{
    private const string GameRecordApiPath = "/event/game_record/card/wapi/getGameRecordCard";

    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<GameRecordApiService> m_Logger;

    public GameRecordApiService(IHttpClientFactory httpClientFactory, ILogger<GameRecordApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<IEnumerable<GameRecordDto>>> GetAsync(GameRecordApiContext context)
    {
        try
        {
            var requestUri = $"{HoYoLabDomains.PublicApi}{GameRecordApiPath}?uid={context.LtUid}";

            m_Logger.LogInformation(LogMessages.ReceivedRequest, requestUri);

            var httpClient = m_HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new()
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri(requestUri)
            };
            request.Headers.Add("Cookie", $"ltoken_v2={context.LToken}; ltuid_v2={context.LtUid}");
            request.Headers.Add("X-Rpc-Language", "en-us");

            // Info-level outbound request (no headers)
            m_Logger.LogInformation(LogMessages.OutboundHttpRequest, request.Method, requestUri);
            var response = await httpClient.SendAsync(request);

            // Info-level inbound response (status only)
            m_Logger.LogInformation(LogMessages.InboundHttpResponse, (int)response.StatusCode, requestUri);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError(LogMessages.NonSuccessStatusCode, response.StatusCode, requestUri);
                return Result<IEnumerable<GameRecordDto>>.Failure(StatusCode.ExternalServerError, "An error occurred", requestUri);
            }

            var json = await response.Content.ReadFromJsonAsync<ApiResponse<UserData>>();

            if (json?.Data == null)
            {
                m_Logger.LogError(LogMessages.FailedToParseResponse, requestUri, context.LtUid.ToString());
                return Result<IEnumerable<GameRecordDto>>.Failure(StatusCode.ExternalServerError, "An error occurred", requestUri);
            }

            // Info-level API retcode after parse
            m_Logger.LogInformation(LogMessages.InboundHttpResponseWithRetcode, (int)response.StatusCode, requestUri, json.Retcode, context.LtUid.ToString());

            if (json.Retcode == -100)
            {
                m_Logger.LogError("Invalid credentials (retcode -100) for ltuid: {LtUid}", context.LtUid);
                return Result<IEnumerable<GameRecordDto>>.Failure(StatusCode.Unauthorized,
                    "Invalid HoYoLAB UID or Cookies. Please re-authenticate", requestUri);
            }

            if (json.Retcode != 0)
            {
                m_Logger.LogError(LogMessages.UnknownRetcode, json.Retcode, context.LtUid.ToString(), requestUri);
                return Result<IEnumerable<GameRecordDto>>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later", requestUri);
            }

            m_Logger.LogInformation(LogMessages.SuccessfullyRetrievedData, requestUri, context.LtUid.ToString());

            var result = json.Data.List.Select(x => new GameRecordDto
            {
                GameId = x.GameId ?? 0,
                Game = x.GameName switch
                {
                    "Genshin Impact" => Game.Genshin,
                    "Honkai Impact 3rd" => Game.HonkaiImpact3,
                    "Honkai: Star Rail" => Game.HonkaiStarRail,
                    "Zenless Zone Zero" => Game.ZenlessZoneZero,
                    "Tears of Themis" => Game.TearsOfThemis,
                    _ => Game.Unsupported
                },
                HasRole = x.HasRole ?? false,
                Nickname = x.Nickname ?? string.Empty,
                Region = x.Region ?? string.Empty,
                Level = x.Level ?? 0
            });

            return Result<IEnumerable<GameRecordDto>>.Success(result ?? [], requestUri: requestUri);
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, LogMessages.ExceptionOccurred,
                $"{HoYoLabDomains.PublicApi}{GameRecordApiPath}", context.LtUid.ToString());
            return Result<IEnumerable<GameRecordDto>>.Failure(StatusCode.BotError, "An error occurred");
        }
    }
}
