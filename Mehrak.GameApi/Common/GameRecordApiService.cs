using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;

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
            m_Logger.LogInformation("Retrieving game record data for user {Uid}", context.UserId);

            var httpClient = m_HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new()
            {
                Method = HttpMethod.Get
            };
            request.Headers.Add("Cookie", $"ltoken_v2={context.LToken}; ltuid_v2={context.LtUid}");
            request.Headers.Add("X-Rpc-Language", "en-us");
            request.RequestUri = new Uri($"{HoYoLabDomains.PublicApi}{GameRecordApiPath}?uid={context.LtUid}");

            m_Logger.LogDebug("Sending request to game record API: {Url}", request.RequestUri);
            var response = await httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogWarning("Game record API returned non-success status code: {StatusCode}",
                    response.StatusCode);
                return Result<IEnumerable<GameRecordDto>>.Failure(StatusCode.ExternalServerError, "An error occurred");
            }

            var json = await response.Content.ReadFromJsonAsync<ApiResponse<UserData>>();
            if (json?.Data == null)
            {
                m_Logger.LogWarning("Failed to retrieve user data for {Uid} - null response", context.UserId);
                return Result<IEnumerable<GameRecordDto>>.Failure(StatusCode.ExternalServerError, "An error occurred");
            }

            m_Logger.LogInformation("Successfully retrieved game record data for user {Uid}", context.UserId);

            var result = json.Data.List.Select(x => new GameRecordDto()
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
                Level = x.Level ?? 0,
            });

            return Result<IEnumerable<GameRecordDto>>.Success(result ?? []);
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Error retrieving game record data for user {Uid}", context.UserId);
            return Result<IEnumerable<GameRecordDto>>.Failure(StatusCode.BotError, "An error occurred");
        }
    }
}
