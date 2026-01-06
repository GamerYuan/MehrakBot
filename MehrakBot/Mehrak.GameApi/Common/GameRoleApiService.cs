#region

using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.Common;

public class GameRoleApiService : IApiService<GameProfileDto, GameRoleApiContext>
{
    private static readonly string GameUserRoleApiPath =
        "/binding/api/getUserGameRolesByLtoken";

    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<GameRoleApiService> m_Logger;

    public GameRoleApiService(IHttpClientFactory httpClientFactory, ILogger<GameRoleApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<GameProfileDto>> GetAsync(GameRoleApiContext context)
    {
        try
        {
            var requestUri =
                $"{HoYoLabDomains.AccountApi}{GameUserRoleApiPath}?game_biz={context.Game.ToGameBizString()}&region={context.Region}";

            m_Logger.LogInformation(LogMessages.PreparingRequest, requestUri);

            var httpClient = m_HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new()
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri(requestUri)
            };
            request.Headers.Add("Cookie", $"ltoken_v2={context.LToken}; ltuid_v2={context.LtUid}");

            // Info-level outbound request (no headers)
            m_Logger.LogInformation(LogMessages.OutboundHttpRequest, request.Method, requestUri);
            var response = await httpClient.SendAsync(request);

            // Info-level inbound response (status only)
            m_Logger.LogInformation(LogMessages.InboundHttpResponse, (int)response.StatusCode, requestUri);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError(LogMessages.NonSuccessStatusCode, response.StatusCode, requestUri);
                return Result<GameProfileDto>.Failure(StatusCode.ExternalServerError, "API returned error status code", requestUri);
            }

            var json = await response.Content.ReadFromJsonAsync<ApiResponse<GameProfileResponse>>();

            if (json == null)
            {
                m_Logger.LogError(LogMessages.EmptyResponseData, requestUri, context.UserId);
                return Result<GameProfileDto>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while retrieving profile information", requestUri);
            }

            if (json.Retcode == -100)
            {
                m_Logger.LogError(LogMessages.InvalidCredentials, context.UserId);
                return Result<GameProfileDto>.Failure(StatusCode.Unauthorized,
                    "Invalid HoYoLAB UID or Cookies. Please re-authenticate", requestUri);
            }

            if (json.Retcode != 0)
            {
                m_Logger.LogError(LogMessages.UnknownRetcode, json.Retcode, context.UserId, requestUri, json);
                return Result<GameProfileDto>.Failure(StatusCode.ExternalServerError,
                    $"An error occurred while retrieving profile information", requestUri);
            }

            if (json.Data?.List == null || json.Data?.List.Count == 0)
            {
                m_Logger.LogWarning("No game data found for User {UserId} profile LtUid {LtUid} on {Region}",
                    context.UserId, context.LtUid, context.Region);
                return Result<GameProfileDto>.Failure(StatusCode.ExternalServerError,
                    "No game information found. Please select the correct region", requestUri);
            }

            // Info-level API retcode after parse (success path)
            m_Logger.LogInformation(LogMessages.InboundHttpResponseWithRetcode, (int)response.StatusCode, requestUri, 0, context.UserId);

            var gameProfile = json?.Data?.List[0];

            GameProfileDto dto = new()
            {
                GameUid = gameProfile?.GameUid ?? string.Empty,
                Nickname = gameProfile?.Nickname ?? string.Empty,
                Level = gameProfile?.Level ?? 0
            };

            m_Logger.LogInformation(LogMessages.SuccessfullyRetrievedData, requestUri, context.UserId);
            return Result<GameProfileDto>.Success(dto, requestUri: requestUri);
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, LogMessages.ExceptionOccurred,
                $"{HoYoLabDomains.AccountApi}{GameUserRoleApiPath}", context.UserId);
            return Result<GameProfileDto>.Failure(StatusCode.BotError,
                "An error occurred while processing the request");
        }
    }

    private sealed class GameProfileResponse
    {
        [JsonPropertyName("list")] public List<GameProfile> List { get; set; } = [];
    }
}
