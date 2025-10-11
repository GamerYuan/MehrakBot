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
            var httpClient = m_HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new()
            {
                Method = HttpMethod.Get
            };
            request.Headers.Add("Cookie", $"ltoken_v2={context.LToken}; ltuid_v2={context.LtUid}");
            request.RequestUri = new Uri($"{HoYoLabDomains.AccountApi}{GameUserRoleApiPath}?" +
                $"game_biz={context.Game.ToGameBizString()}&region={context.Region}");

            m_Logger.LogDebug("Sending request to game roles API: {Url}", request.RequestUri);
            var response = await httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogWarning("Game roles API returned non-success status code: {StatusCode}",
                    response.StatusCode);
                return Result<GameProfileDto>.Failure(StatusCode.ExternalServerError, "API returned error status code");
            }

            var node = await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync());

            if (node?["retcode"]?.GetValue<int>() == -100)
            {
                m_Logger.LogWarning("Invalid ltoken or ltuid for user {Uid} on {Region}",
                   context.UserId, context.Region);
                return Result<GameProfileDto>.Failure(StatusCode.Unauthorized,
                    "Invalid HoYoLAB UID or Cookies. Please re-authenticate");
            }

            if (node?["retcode"]?.GetValue<int>() != 0)
            {
                m_Logger.LogWarning("Game roles API returned error code: {Retcode} - {Message}",
                    node?["retcode"], node?["message"]);
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

            return Result<GameProfileDto>.Success(dto);
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Error retrieving game UID for user {Uid} on {Region}", context.UserId, context.Region);
            return Result<GameProfileDto>.Failure(StatusCode.BotError, "An error occurred while processing the request");
        }
    }
}
