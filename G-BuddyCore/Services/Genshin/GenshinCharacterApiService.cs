#region

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

#endregion

namespace G_BuddyCore.Services.Genshin;

public class GenshinCharacterApiService : ICharacterApi
{
    private const string BaseUrl = "https://sg-public-api.hoyolab.com/event/game_record/genshin/api";

    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly GameRecordApiService m_GameRecordApiService;

    public GenshinCharacterApiService(IHttpClientFactory httpClientFactory, GameRecordApiService gameRecordApiService)
    {
        m_HttpClientFactory = httpClientFactory;
        m_GameRecordApiService = gameRecordApiService;
    }

    public async Task<string> GetAllCharactersAsync(ulong uid, string ltoken)
    {
        var gameRecordCard = await m_GameRecordApiService.GetGameRecordCardJsonAsync(uid, ltoken);
        var node = gameRecordCard?["list"]?.AsArray().FirstOrDefault(x => (int)(x?["game_id"] ?? 0) == 2);

        if (node == null) throw new Exception("Failed to get game record card");
        var payload = new CharacterListPayload
        {
            RoleId = node["game_role_id"]?.GetValue<string>() ?? string.Empty,
            Server = node["region"]?.GetValue<string>() ?? string.Empty,
            SortType = 1
        };
        var httpClient = m_HttpClientFactory.CreateClient();
        JsonSerializerOptions options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };
        var str = JsonSerializer.Serialize(payload, options);

        HttpRequestMessage request = new();
        request.Method = HttpMethod.Post;
        request.Headers.Add("Cookie", $"ltuid_v2={uid}; ltoken_v2={ltoken}");
        request.Headers.Add("X-Rpc-Language", "en-us");
        request.RequestUri = new Uri($"{BaseUrl}/character/list");
        request.Content =
            new StringContent(JsonSerializer.Serialize(payload, options), Encoding.UTF8, "application/json");

        var response = await httpClient.SendAsync(request);
        var json = (await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync()))?.AsObject();
        var data = json?["data"];
        if (data?["list"] == null) throw new JsonException("Failed to deserialize response");

        var entries = data["list"]?.AsArray();
        if (entries == null) throw new JsonException("Failed to deserialize response");

        var characterList = string.Join(", ", entries.Select(x => x?["name"]?.ToString() ?? string.Empty));

        return characterList;
    }

    public Task<string> GetCharacterDataFromNameAsync(ulong uid, string ltoken, string characterName)
    {
        throw new NotImplementedException();
    }

    public Task<string> GetCharacterDataFromIdAsync(ulong uid, string ltoken, uint characterId)
    {
        throw new NotImplementedException();
    }
}
