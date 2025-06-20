#region

using System.Net;
using System.Text.Json;
using MehrakCore.ApiResponseTypes.Hsr;
using MehrakCore.Models;
using MehrakCore.Utility;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

#endregion

namespace MehrakCore.Services.Commands.Hsr.Character;

public class HsrCharacterApiService : ICharacterApi<HsrBasicCharacterData, HsrCharacterInformation>
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<HsrCharacterApiService> m_Logger;
    private readonly IMemoryCache m_MemoryCache;

    private const string ApiUrl = "https://sg-public-api.hoyolab.com/event/game_record/hkrpg/api/avatar/info";
    private const int CacheExpirationMinutes = 10;

    public HsrCharacterApiService(IHttpClientFactory httpClientFactory, IMemoryCache memoryCache,
        ILogger<HsrCharacterApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
        m_MemoryCache = memoryCache;
    }

    public async Task<IEnumerable<HsrBasicCharacterData>> GetAllCharactersAsync(ulong uid, string ltoken,
        string gameUid, string region)
    {
        var cacheKey = $"hsr_characters_{gameUid}";

        // Try to get data from cache first
        if (m_MemoryCache.TryGetValue(cacheKey, out IEnumerable<HsrBasicCharacterData>? cachedData))
        {
            m_Logger.LogInformation("Retrieved character data from cache for game UID: {GameUid}", gameUid);
            return cachedData!;
        }

        var client = m_HttpClientFactory.CreateClient("Default");
        m_Logger.LogInformation("Retrieving character list for user {Uid} on {Region} server (game UID: {GameUid})",
            uid, region, gameUid);
        HttpRequestMessage request = new()
        {
            Method = HttpMethod.Get,
            RequestUri = new Uri($"{ApiUrl}?server={region}&role_id={gameUid}&need_wiki=true"),
            Headers =
            {
                { "Cookie", $"ltuid_v2={uid}; ltoken_v2={ltoken}" },
                { "X-Rpc-Client_type", "5" },
                { "X-Rpc-App_version", "1.5.0" },
                { "X-Rpc-Language", "en-us" },
                { "DS", DSGenerator.GenerateDS() }
            }
        };
        m_Logger.LogDebug("Sending character list request to {Endpoint}", request.RequestUri);
        var response = await client.SendAsync(request);
        var data = await JsonSerializer.DeserializeAsync<CharacterListApiResponse>(
            await response.Content.ReadAsStreamAsync());

        if (!response.IsSuccessStatusCode)
        {
            m_Logger.LogError("Error sending character list request to {Endpoint}", request.RequestUri);
            return [];
        }

        if (data?.HsrBasicCharacterData?.AvatarList == null)
        {
            m_Logger.LogWarning("No character data found for user {Uid} on {Region} server (game UID: {GameUid})",
                uid, region, gameUid);
            return [];
        }

        m_Logger.LogInformation(
            "Successfully retrieved {Count} characters for user {Uid} on {Region} server (game UID: {GameUid})",
            data.HsrBasicCharacterData.AvatarList.Count, uid, region, gameUid);

        var result = new[] { data.HsrBasicCharacterData };

        var cacheOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(CacheExpirationMinutes));

        m_MemoryCache.Set(cacheKey, result, cacheOptions);
        m_Logger.LogInformation("Cached character data for game UID: {GameUid} for {Minutes} minutes",
            gameUid, CacheExpirationMinutes);

        return result;
    }

    public async Task<ApiResult<HsrCharacterInformation>> GetCharacterDataFromIdAsync(ulong uid, string ltoken,
        string gameUid, string region, uint characterId)
    {
        m_Logger.LogInformation("Retrieving character data for user {Uid} on {Region} server (game UID: {GameUid})",
            uid, region, gameUid);
        var characterList = await GetAllCharactersAsync(uid, ltoken, gameUid, region);
        var character = characterList.FirstOrDefault()?.AvatarList?.FirstOrDefault(x => x.Id == characterId);
        if (character == null)
            return ApiResult<HsrCharacterInformation>.Failure(HttpStatusCode.BadRequest,
                $"Character with ID {characterId} not found for user {uid} on {region} server (game UID: {gameUid})");
        m_Logger.LogInformation(
            "Successfully retrieved character data for user {Uid} on {Region} server (game UID: {GameUid})",
            uid, region, gameUid);
        return ApiResult<HsrCharacterInformation>.Success(character);
    }

    public async Task<ApiResult<HsrCharacterInformation>> GetCharacterDataFromNameAsync(ulong uid, string ltoken,
        string gameUid, string region, string characterName)
    {
        m_Logger.LogInformation("Retrieving character data for user {Uid} on {Region} server (game UID: {GameUid})",
            uid, region, gameUid);
        var characterList = await GetAllCharactersAsync(uid, ltoken, gameUid, region);
        var character =
            characterList.FirstOrDefault()?.AvatarList?
                .FirstOrDefault(x => x.Name!.Equals(characterName, StringComparison.OrdinalIgnoreCase));
        if (character == null)
            return ApiResult<HsrCharacterInformation>.Failure(HttpStatusCode.BadRequest,
                $"Character with name {characterName} not found for user {uid} on {region} server (game UID: {gameUid})");
        m_Logger.LogInformation(
            "Successfully retrieved character data for user {Uid} on {Region} server (game UID: {GameUid})",
            uid, region, gameUid);
        return ApiResult<HsrCharacterInformation>.Success(character);
    }
}
