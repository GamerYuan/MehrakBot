#region

using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MehrakCore.ApiResponseTypes;
using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

#endregion

namespace MehrakCore.Services.Commands.Genshin.Character;

public class GenshinCharacterApiService : ICharacterApi<GenshinBasicCharacterData, GenshinCharacterDetail>
{
    private const string BaseUrl = "https://sg-public-api.hoyolab.com/event/game_record/genshin/api";

    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<GenshinCharacterApiService> m_Logger;
    private readonly IMemoryCache m_Cache;
    private const int CacheExpirationMinutes = 10;

    public GenshinCharacterApiService(IMemoryCache cache,
        IHttpClientFactory httpClientFactory,
        ILogger<GenshinCharacterApiService> logger)
    {
        m_Cache = cache;
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<IEnumerable<GenshinBasicCharacterData>> GetAllCharactersAsync(ulong uid, string ltoken,
        string gameUid, string region)
    {
        m_Logger.LogInformation("Retrieving character list for user {Uid} on {Region} server (game UID: {GameUid})",
            uid, region, gameUid);
        var cacheKey = $"genshin_characters_{gameUid}";

        if (m_Cache.TryGetValue(cacheKey, out IEnumerable<GenshinBasicCharacterData>? cachedEntry))
        {
            m_Logger.LogInformation("Retrieved character data from cache for game UID: {GameUid}", gameUid);
            return cachedEntry!;
        }

        var payload = new CharacterListPayload
        (
            gameUid,
            region,
            1
        );
        var httpClient = m_HttpClientFactory.CreateClient("Default");

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

        var cacheOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(CacheExpirationMinutes));
        m_Cache.Set(cacheKey, data.List, cacheOptions);
        m_Logger.LogInformation("Cached character data for game UID: {GameUid} for {Minutes} minutes",
            gameUid, CacheExpirationMinutes);

        return data.List;
    }

    public async Task<ApiResult<GenshinCharacterDetail>> GetCharacterDataFromIdAsync(ulong uid, string ltoken,
        string gameUid, string region, uint characterId)
    {
        m_Logger.LogInformation(
            "Retrieving character data for {CharacterId} for user {Uid} on {Region} server (game UID: {GameUid})",
            characterId, uid, region, gameUid);

        var payload = new CharacterDetailPayload
        (
            gameUid,
            region,
            new ReadOnlyCollection<uint>([characterId])
        );
        var httpClient = m_HttpClientFactory.CreateClient("Default");

        HttpRequestMessage request = new();
        request.Method = HttpMethod.Post;
        request.Headers.Add("Cookie", $"ltuid_v2={uid}; ltoken_v2={ltoken}");
        request.Headers.Add("X-Rpc-Language", "en-us");
        request.RequestUri = new Uri($"{BaseUrl}/character/detail");
        request.Content =
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        m_Logger.LogDebug("Sending character detail request to {Endpoint}", request.RequestUri);
        var response = await httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            m_Logger.LogWarning("Character detail API returned non-success status code: {StatusCode}",
                response.StatusCode);
            return ApiResult<GenshinCharacterDetail>.Failure(response.StatusCode,
                $"Failed to retrieve character data. Status code: {response.StatusCode}");
        }

        var json = await response.Content.ReadFromJsonAsync<CharacterDetailApiResponse>();
        var data = json?.Data;

        if (data?.List == null)
        {
            m_Logger.LogError("Failed to deserialize character detail response for user {Uid}", uid);
            throw new JsonException("Failed to deserialize response");
        }

        return ApiResult<GenshinCharacterDetail>.Success(data, json?.Retcode ?? 0, HttpStatusCode.Accepted);
    }

    public async Task<ApiResult<GenshinCharacterDetail>> GetCharacterDataFromNameAsync(ulong uid, string ltoken,
        string gameUid, string region, string characterName)
    {
        m_Logger.LogInformation(
            "Retrieving character data for {CharacterName} for user {Uid} on {Region} server (game UID: {GameUid})",
            characterName, uid, region, gameUid);

        var characters = await GetAllCharactersAsync(uid, ltoken, gameUid, region);
        var character =
            characters.FirstOrDefault(x => x.Name.Equals(characterName, StringComparison.OrdinalIgnoreCase));

        if (character == null)
        {
            m_Logger.LogWarning("Character {CharacterName} not found for user {Uid}", characterName, uid);
            throw new KeyNotFoundException($"Character '{characterName}' not found");
        }

        var result = await GetCharacterDataFromIdAsync(uid, ltoken, gameUid, region, (uint)character.Id!);
        if (!result.IsSuccess)
        {
            m_Logger.LogError("Failed to retrieve data for character {CharacterName} for user {Uid}",
                characterName, uid);
            throw new Exception($"Failed to retrieve data for character '{characterName}'");
        }

        return ApiResult<GenshinCharacterDetail>.Success(result.Data);
    }
}
