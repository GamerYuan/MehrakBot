#region

using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;

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

            m_Logger.LogInformation(LogMessages.ReceivedRequest, requestUri);

            var httpClient = m_HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new()
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri(requestUri)
            };
            request.Headers.Add("Cookie", $"ltoken_v2={context.LToken}; ltuid_v2={context.LtUid}");

            m_Logger.LogDebug(LogMessages.SendingRequest, requestUri);
            var response = await httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError(LogMessages.NonSuccessStatusCode, response.StatusCode, requestUri);
                return Result<GameProfileDto>.Failure(StatusCode.ExternalServerError, "API returned error status code");
            }

            var node = await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync());

            if (node?["retcode"]?.GetValue<int>() == -100)
            {
                m_Logger.LogError("Invalid credentials (retcode -100) for ltuid: {LtUid}", context.LtUid);
                return Result<GameProfileDto>.Failure(StatusCode.Unauthorized,
                    "Invalid HoYoLAB UID or Cookies. Please re-authenticate");
            }

            if (node?["retcode"]?.GetValue<int>() != 0)
            {
                m_Logger.LogError(LogMessages.UnknownRetcode, node?["retcode"]?.GetValue<int>() ?? -1,
                    context.LtUid.ToString(), requestUri);
                return Result<GameProfileDto>.Failure(StatusCode.ExternalServerError,
                    $"An error occurred while retrieving profile information");
            }

            if (node["data"]?["list"] == null || node["data"]?["list"]?.AsArray().Count == 0)
            {
                m_Logger.LogWarning("No game data found for user {Uid} on {Region}", context.UserId, context.Region);
                return Result<GameProfileDto>.Failure(StatusCode.ExternalServerError,
                    "No game information found. Please select the correct region");
            }

            var gameProfile = node["data"]?["list"]?[0].Deserialize<GameProfile>();

            GameProfileDto dto = new()
            {
                GameUid = gameProfile?.GameUid ?? string.Empty,
                Nickname = gameProfile?.Nickname ?? string.Empty,
                Level = gameProfile?.Level ?? 0,
            };

            m_Logger.LogInformation(LogMessages.SuccessfullyRetrievedData, requestUri, dto.GameUid);
            return Result<GameProfileDto>.Success(dto);
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, LogMessages.ExceptionOccurred,
                $"{HoYoLabDomains.AccountApi}{GameUserRoleApiPath}", context.LtUid.ToString());
            return Result<GameProfileDto>.Failure(StatusCode.BotError,
                "An error occurred while processing the request");
        }
    }
}
