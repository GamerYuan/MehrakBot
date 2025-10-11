#region

using MehrakCore.ApiResponseTypes;
using MehrakCore.Constants;
using MehrakCore.Models;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

#endregion

namespace MehrakCore.Services.Common;

public class GameRecordApiService : IApiService<object>
{
    private static readonly string GameRecordApiPath = "/event/game_record/card/wapi/getGameRecordCard";

    private static readonly string GameUserRoleApiPath =
        "/binding/api/getUserGameRolesByLtoken";

    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<GameRecordApiService> m_Logger;

    public GameRecordApiService(IHttpClientFactory httpClientFactory, ILogger<GameRecordApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<UserData?> GetUserDataAsync(ulong uid, string ltoken)
    {
        try
        {
            m_Logger.LogInformation("Retrieving game record data for user {Uid}", uid);

            var httpClient = m_HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new();
            request.Method = HttpMethod.Get;
            request.Headers.Add("Cookie", $"ltoken_v2={ltoken}; ltuid_v2={uid}");
            request.Headers.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537.3");
            request.RequestUri = new Uri($"{HoYoLabDomains.PublicApi}{GameRecordApiPath}?uid={uid}");

            m_Logger.LogDebug("Sending request to game record API: {Url}", request.RequestUri);
            var response = await httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogWarning("Game record API returned non-success status code: {StatusCode}",
                    response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadFromJsonAsync<GameRecordCardApiResponse>();
            if (json?.Data == null)
            {
                m_Logger.LogWarning("Failed to retrieve user data for {Uid} - null response", uid);
                return null;
            }

            m_Logger.LogInformation("Successfully retrieved game record data for user {Uid}", uid);
            return json.Data;
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Error retrieving game record data for user {Uid}", uid);
            throw;
        }
    }

    /// <summary>
    /// Return Game UID for the specified game and region
    /// </summary>
    /// <param name="uid">LTUID V2</param>
    /// <param name="ltoken">LToken V2</param>
    /// <param name="gameIdentifier">Game identifier</param>
    /// <param name="region">Region string</param>
    /// <returns>ApiResult containing the game UID and HTTP status code</returns>
    public async Task<Result<UserGameData>> GetUserGameDataAsync(ulong uid, string ltoken, string gameIdentifier,
        string region)
    {
        try
        {
            m_Logger.LogInformation("Retrieving game UID for user {Uid} on {Region} server (game: {GameId})",
                uid, region, gameIdentifier);

            var httpClient = m_HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new();
            request.Method = HttpMethod.Get;
            request.Headers.Add("Cookie", $"ltoken_v2={ltoken}; ltuid_v2={uid}");
            request.Headers.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537.3");
            request.RequestUri = new Uri($"{HoYoLabDomains.AccountApi}{GameUserRoleApiPath}?game_biz={gameIdentifier}&region={region}");

            m_Logger.LogDebug("Sending request to game roles API: {Url}", request.RequestUri);
            var response = await httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogWarning("Game roles API returned non-success status code: {StatusCode}",
                    response.StatusCode);
                return Result<UserGameData>.Failure(response.StatusCode, "API returned error status code");
            }

            var node = await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync());

            if (node?["retcode"]?.GetValue<int>() == -100)
            {
                m_Logger.LogWarning("Invalid ltoken or ltuid for user {Uid} on {Region}",
                    uid, region);
                return Result<UserGameData>.Failure(HttpStatusCode.Unauthorized,
                    "Invalid HoYoLAB UID or Cookies. Please re-authenticate");
            }

            if (node?["retcode"]?.GetValue<int>() != 0)
            {
                m_Logger.LogWarning("Game roles API returned error code: {Retcode} - {Message}",
                    node?["retcode"], node?["message"]);
                return Result<UserGameData>.Failure(HttpStatusCode.InternalServerError,
                    $"An error occurred while retrieving profile information");
            }

            if (node["data"]?["list"] == null || node["data"]?["list"]?.AsArray().Count == 0)
            {
                m_Logger.LogWarning("No game data found for user {Uid} on {Region}", uid, region);
                return Result<UserGameData>.Failure(HttpStatusCode.NotFound,
                    "No game information found. Please select the correct region");
            }

            var gameUid = node["data"]?["list"]?[0].Deserialize<UserGameData>();

            m_Logger.LogInformation("Successfully retrieved game UID {GameUid} for user {Uid} on {Region}",
                gameUid, uid, region);
            return Result<UserGameData>.Success(gameUid!, node?["retcode"]?.GetValue<int>() ?? 0,
                response.StatusCode);
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Error retrieving game UID for user {Uid} on {Region}", uid, region);
            throw;
        }
    }
}
