#region

using System.Net.Http.Json;
using Mehrak.Domain.Character.Models;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.GameApi.Shared;
using Mehrak.GameApi.Shared.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.GameRecord;

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

    public async Task<Result<IEnumerable<GameRecordDto>>> GetAsync(GameRecordApiContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(IApiService.MaxTimeoutSeconds));

            var requestUri = $"{HoYoLabDomains.PublicApi}{GameRecordApiPath}?uid={context.LtUid}";

            m_Logger.LogInformation(LogMessages.PreparingRequest, requestUri);

            var httpClient = m_HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new()
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri(requestUri)
            };
            request.Headers.Add("Cookie", $"ltoken_v2={context.LToken}; ltuid_v2={context.LtUid}");
            request.Headers.Add("X-Rpc-Language", "en-us");

            var response = await httpClient.SendAsync(request, timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError(LogMessages.NonSuccessStatusCode, response.StatusCode, requestUri);
                return Result<IEnumerable<GameRecordDto>>.Failure(StatusCode.ExternalServerError, "An error occurred", requestUri);
            }

            var json = await response.Content.ReadFromJsonAsync<ApiResponse<UserData>>(timeoutCts.Token);

            if (json?.Data == null)
            {
                m_Logger.LogError(LogMessages.EmptyResponseData, requestUri, context.UserId);
                return Result<IEnumerable<GameRecordDto>>.Failure(StatusCode.ExternalServerError, "An error occurred", requestUri);
            }

            // Info-level API retcode after parse
            m_Logger.LogInformation(LogMessages.InboundHttpResponseWithRetcode, (int)response.StatusCode, requestUri, json.Retcode,
                context.UserId);

            if (json.Retcode == -100)
            {
                m_Logger.LogError(LogMessages.InvalidCredentials, context.UserId);
                return Result<IEnumerable<GameRecordDto>>.Failure(StatusCode.Unauthorized,
                    "Invalid HoYoLAB UID or Cookies. Please re-authenticate", requestUri);
            }

            if (json.Retcode != 0)
            {
                m_Logger.LogError(LogMessages.UnknownRetcode, json.Retcode, context.UserId, requestUri, json);
                return Result<IEnumerable<GameRecordDto>>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later", requestUri);
            }

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
        catch (OperationCanceledException)
        {
            return Result<IEnumerable<GameRecordDto>>.FromCancellation(cancellationToken);
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, LogMessages.ExceptionOccurred,
                $"{HoYoLabDomains.PublicApi}{GameRecordApiPath}", context.UserId);
            return Result<IEnumerable<GameRecordDto>>.Failure(StatusCode.BotError, "An error occurred");
        }
    }
}
