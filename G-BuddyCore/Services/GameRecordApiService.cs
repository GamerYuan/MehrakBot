#region

using System.Text.Json;
using System.Text.Json.Nodes;

#endregion

namespace G_BuddyCore.Services;

public class GameRecordApiService
{
    private const string ApiUrl =
        "https://sg-public-api.hoyolab.com/event/game_record/card/wapi/getGameRecordCard";

    private readonly IHttpClientFactory m_HttpClientFactory;

    public GameRecordApiService(IHttpClientFactory httpClientFactory)
    {
        m_HttpClientFactory = httpClientFactory;
    }

    public async Task<string> GetGameRecordCardAsync(ulong uid, string ltoken)
    {
        var gameRecordCard = await GetGameRecordCardJsonAsync(uid, ltoken);
        if (gameRecordCard == null) throw new JsonException("Failed to deserialize response");

        var characterList = string.Join(", ", gameRecordCard["list"]?.AsArray()
            .Select(x => x?["name"]?.ToString() ?? string.Empty) ?? []);

        return characterList;
    }

    public async Task<JsonNode?> GetGameRecordCardJsonAsync(ulong uid, string ltoken)
    {
        var httpClient = m_HttpClientFactory.CreateClient();
        HttpRequestMessage request = new();
        request.Method = HttpMethod.Get;
        request.Headers.Add("Cookie", $"ltoken_v2={ltoken}; ltuid_v2={uid}");
        request.Headers.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537.3");
        request.RequestUri = new Uri($"{ApiUrl}?uid={uid}");

        var response = await httpClient.SendAsync(request);
        var json = (await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync()))?.AsObject();
        return json?["data"];
    }
}
