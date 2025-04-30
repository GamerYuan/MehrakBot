#region

using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using G_BuddyCore.ApiResponseTypes.Genshin;
using Microsoft.Extensions.Logging;

#endregion

namespace G_BuddyCore.Services.Genshin;

public class GenshinCharacterApiService : ICharacterApi
{
    private const string BaseUrl = "https://sg-public-api.hoyolab.com/event/game_record/genshin/api";

    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly GameRecordApiService m_GameRecordApiService;
    private readonly ILogger<GenshinCharacterApiService> m_Logger;

    public GenshinCharacterApiService(
        IHttpClientFactory httpClientFactory,
        GameRecordApiService gameRecordApiService,
        ILogger<GenshinCharacterApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_GameRecordApiService = gameRecordApiService;
        m_Logger = logger;
    }

    public async Task<IEnumerable<BasicCharacterData>> GetAllCharactersAsync(ulong uid, string ltoken, string gameUid,
        string region)
    {
        try
        {
            m_Logger.LogInformation("Retrieving character list for user {Uid} on {Region} server (game UID: {GameUid})",
                uid, region, gameUid);

            var payload = new CharacterListPayload
            (
                gameUid,
                region,
                1
            );
            var httpClient = m_HttpClientFactory.CreateClient();

            HttpRequestMessage request = new();
            request.Method = HttpMethod.Post;
            request.Headers.Add("Cookie", $"ltuid_v2={uid}; ltoken_v2={ltoken}");
            request.Headers.Add("X-Rpc-Language", "en-us");
            request.RequestUri = new Uri($"{BaseUrl}/character/list");
            request.Content =
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            m_Logger.LogDebug("Sending character list request to {Endpoint}", request.RequestUri);
            var response = await httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
                m_Logger.LogWarning("Character list API returned non-success status code: {StatusCode}",
                    response.StatusCode);

            var json = await response.Content.ReadFromJsonAsync<CharacterListApiResponse>();
            var data = json?.Data;

            if (data?.List == null)
            {
                m_Logger.LogError("Failed to deserialize character list response for user {Uid}", uid);
                throw new JsonException("Failed to deserialize response");
            }

            m_Logger.LogInformation("Successfully retrieved {CharacterCount} characters for user {Uid}",
                data.List.Count, uid);

            return data.List;
        }
        catch (Exception ex) when (ex is not JsonException)
        {
            m_Logger.LogError(ex, "Error retrieving character list for user {Uid} on {Region}", uid, region);
            throw;
        }
    }

    public Task<string> GetCharacterDataFromNameAsync(ulong uid, string ltoken, string gameUid, string region,
        string characterName)
    {
        m_Logger.LogWarning("GetCharacterDataFromNameAsync not implemented");
        throw new NotImplementedException();
    }

    public Task<string> GetCharacterDataFromIdAsync(ulong uid, string ltoken, string gameUid, string region,
        uint characterId)
    {
        m_Logger.LogWarning("GetCharacterDataFromIdAsync not implemented");
        throw new NotImplementedException();
    }
}
