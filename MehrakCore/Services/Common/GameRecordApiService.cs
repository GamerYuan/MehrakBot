#region

using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.Json.Nodes;
using MehrakCore.ApiResponseTypes;
using MehrakCore.Models;
using Microsoft.Extensions.Logging;

#endregion

namespace MehrakCore.Services.Common;

public class GameRecordApiService : IApiService<object>
{
    private const string GameRecordApiUrl =
        "https://sg-public-api.hoyolab.com/event/game_record/card/wapi/getGameRecordCard";

    private const string GameUserRoleApiUrl =
        "https://api-account-os.hoyolab.com/binding/api/getUserGameRolesByLtoken";

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
            request.RequestUri = new Uri($"{GameRecordApiUrl}?uid={uid}");

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
    public async Task<ApiResult<UserGameData>> GetUserGameDataAsync(ulong uid, string ltoken, string gameIdentifier,
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
            request.RequestUri = new Uri($"{GameUserRoleApiUrl}?game_biz={gameIdentifier}&region={region}");

            m_Logger.LogDebug("Sending request to game roles API: {Url}", request.RequestUri);
            var response = await httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogWarning("Game roles API returned non-success status code: {StatusCode}",
                    response.StatusCode);
                return ApiResult<UserGameData>.Failure(response.StatusCode, "API returned error status code");
            }

            var node = await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync());
            var gameUid = node?["data"]?["list"]?[0].Deserialize<UserGameData>();

            m_Logger.LogInformation("Successfully retrieved game UID {GameUid} for user {Uid} on {Region}",
                gameUid, uid, region);
            return ApiResult<UserGameData>.Success(gameUid!, node?["retcode"]?.GetValue<int>() ?? 0,
                response.StatusCode);
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Error retrieving game UID for user {Uid} on {Region}", uid, region);
            throw;
        }
    }

    public async Task<IEnumerable<(string, bool)>> GetApiStatusAsync()
    {
        Ping gameRecordPing = new();
        Ping gameUserRolePing = new();

        var tasks = new List<(string, bool)>
        {
            ("Game Record API",
                (await gameRecordPing.SendPingAsync(new Uri(GameRecordApiUrl).Host)).Status == IPStatus.Success),
            ("Game User Role API",
                (await gameUserRolePing.SendPingAsync(new Uri(GameUserRoleApiUrl).Host)).Status == IPStatus.Success)
        };

        return tasks;
    }
}
