#region

using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using G_BuddyCore.ApiResponseTypes.Genshin;

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
        var gameRecordCard = await m_GameRecordApiService.GetUserDataAsync(uid, ltoken);
        Console.WriteLine(gameRecordCard);
        var node = gameRecordCard?.List.FirstOrDefault(x => x.GameId == 2);

        if (node == null) throw new Exception("Failed to get game record card");
        var payload = new CharacterListPayload
        (
            node.GameRoleId,
            node.Region,
            1
        );
        var httpClient = m_HttpClientFactory.CreateClient();
        var str = JsonSerializer.Serialize(payload);

        HttpRequestMessage request = new();
        request.Method = HttpMethod.Post;
        request.Headers.Add("Cookie", $"ltuid_v2={uid}; ltoken_v2={ltoken}");
        request.Headers.Add("X-Rpc-Language", "en-us");
        request.RequestUri = new Uri($"{BaseUrl}/character/list");
        request.Content =
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await httpClient.SendAsync(request);
        var json = await response.Content.ReadFromJsonAsync<CharacterListApiResponse>();
        var data = json?.Data;
        if (data?.List == null) throw new JsonException("Failed to deserialize response");

        var characterList = string.Join(", ", data.List.Select(x => x.Name.ToString()));

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
