#region

using System.Net.Http.Json;
using System.Text.Json.Nodes;
using G_BuddyCore.ApiResponseTypes;

#endregion

namespace G_BuddyCore.Services;

public class GameRecordApiService
{
    private const string GameRecordApiUrl =
        "https://sg-public-api.hoyolab.com/event/game_record/card/wapi/getGameRecordCard";

    private const string GameUserRoleApiUrl =
        "https://api-account-os.hoyolab.com/binding/api/getUserGameRolesByLtoken";

    private readonly IHttpClientFactory m_HttpClientFactory;

    public GameRecordApiService(IHttpClientFactory httpClientFactory)
    {
        m_HttpClientFactory = httpClientFactory;
    }

    public async Task<UserData?> GetUserDataAsync(ulong uid, string ltoken)
    {
        var httpClient = m_HttpClientFactory.CreateClient();
        HttpRequestMessage request = new();
        request.Method = HttpMethod.Get;
        request.Headers.Add("Cookie", $"ltoken_v2={ltoken}; ltuid_v2={uid}");
        request.Headers.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537.3");
        request.RequestUri = new Uri($"{GameRecordApiUrl}?uid={uid}");

        var response = await httpClient.SendAsync(request);
        var json = await response.Content.ReadFromJsonAsync<GameRecordCardApiResponse>();
        return json?.Data;
    }

    /// <summary>
    /// Return Game UID for the specified game and region
    /// </summary>
    /// <param name="uid">LTUID V2</param>
    /// <param name="ltoken">LToken V2</param>
    /// <param name="gameIdentitfier">Game identifier</param>
    /// <param name="region">Region string</param>
    /// <returns></returns>
    public async Task<string?> GetUserRegionUidAsync(ulong uid, string ltoken, string gameIdentitfier, string region)
    {
        var httpClient = m_HttpClientFactory.CreateClient();
        HttpRequestMessage request = new();
        request.Method = HttpMethod.Get;
        request.Headers.Add("Cookie", $"ltoken_v2={ltoken}; ltuid_v2={uid}");
        request.Headers.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537.3");
        request.RequestUri = new Uri($"{GameUserRoleApiUrl}?game_biz={gameIdentitfier}&region={region}");

        var response = await httpClient.SendAsync(request);
        var node = await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync());
        return node?["data"]?["list"]?[0]?["game_uid"]?.GetValue<string>();
    }
}
