using MehrakCore.ApiResponseTypes.Zzz;
using MehrakCore.Constants;
using MehrakCore.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MehrakCore.Services.Commands.Zzz.Character;

internal class ZzzCharacterApiService : ICharacterApi<ZzzBasicAvatarData, ZzzFullAvatarData>
{
    private const string BasePath = "/event/game_record_zzz/api/zzz/avatar";

    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<ZzzCharacterApiService> m_Logger;
    private readonly IMemoryCache m_MemoryCache;
    private const int CacheExpirationMinutes = 10;

    private static JsonSerializerOptions JsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public ZzzCharacterApiService(IMemoryCache memoryCache,
        IHttpClientFactory httpClientFactory,
        ILogger<ZzzCharacterApiService> logger)
    {
        m_MemoryCache = memoryCache;
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<IEnumerable<ZzzBasicAvatarData>> GetAllCharactersAsync(ulong uid,
        string ltoken, string gameUid, string region)
    {
        try
        {
            m_Logger.LogInformation("Retrieving character list for user {Uid} on {Region} server (game UID: {GameUid})",
                uid, region, gameUid);
            string cacheKey = $"zzz_characters_{gameUid}";

            if (m_MemoryCache.TryGetValue(cacheKey, out IEnumerable<ZzzBasicAvatarData>? cachedEntry))
            {
                m_Logger.LogInformation("Retrieved character data from cache for game UID: {GameUid}", gameUid);
                return cachedEntry!;
            }

            HttpClient httpClient = m_HttpClientFactory.CreateClient("Default");

            HttpRequestMessage request = new();
            request.Headers.Add("Cookie", $"ltuid_v2={uid}; ltoken_v2={ltoken}");
            request.Headers.Add("X-Rpc-Language", "en-us");
            request.RequestUri = new Uri($"{HoYoLabDomains.PublicApi}{BasePath}/basic?server={region}&role_id={gameUid}");

            m_Logger.LogDebug("Sending character list request to {Endpoint}", request.RequestUri);
            HttpResponseMessage response = await httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
                m_Logger.LogWarning("Character list API returned non-success status code: {StatusCode}",
                    response.StatusCode);

            JsonNode? json = await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync());
            JsonNode? data = json?["data"];

            if (data?["avatar_list"] == null)
            {
                m_Logger.LogError("Failed to deserialize character list response for user {Uid}", uid);
                throw new JsonException("Failed to deserialize response");
            }

            ZzzBasicAvatarData[]? avatarList = data["avatar_list"].Deserialize<ZzzBasicAvatarData[]>();

            if (avatarList == null)
            {
                m_Logger.LogError("Failed to deserialize character list response for user {Uid}", uid);
                throw new JsonException("Failed to deserialize response");
            }

            m_Logger.LogInformation("Successfully retrieved {CharacterCount} characters for user {Uid}",
                avatarList.Length, uid);

            MemoryCacheEntryOptions cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(CacheExpirationMinutes));
            m_MemoryCache.Set(cacheKey, avatarList, cacheOptions);
            m_Logger.LogInformation("Cached character data for game UID: {GameUid} for {Minutes} minutes",
                gameUid, CacheExpirationMinutes);

            return avatarList;
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Failed to retrieve character list for user {Uid} on {Region} server (game UID: {GameUid})",
                uid, region, gameUid);
            throw new CommandException("An error occurred while retrieving character data", e);
        }
    }

    public async Task<Result<ZzzFullAvatarData>> GetCharacterDataFromIdAsync(ulong uid, string ltoken,
        string gameUid, string region, uint characterId)
    {
        try
        {
            m_Logger.LogInformation(
                "Retrieving character data for {CharacterId} for user {Uid} on {Region} server (game UID: {GameUid})",
                characterId, uid, region, gameUid);

            HttpClient httpClient = m_HttpClientFactory.CreateClient("Default");

            HttpRequestMessage request = new();
            request.Headers.Add("Cookie", $"ltuid_v2={uid}; ltoken_v2={ltoken}");
            request.Headers.Add("X-Rpc-Language", "en-us");
            request.RequestUri = new Uri($"{HoYoLabDomains.PublicApi}{BasePath}" +
                $"/info?id_list[]={characterId}&server={region}&role_id={gameUid}&need_wiki=true");
            m_Logger.LogDebug("Sending character detail request to {Endpoint}", request.RequestUri);
            HttpResponseMessage response = await httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogWarning("Character detail API returned non-success status code: {StatusCode}",
                    response.StatusCode);
                return Result<ZzzFullAvatarData>.Failure(response.StatusCode,
                    "An error occurred while retrieving character data");
            }

            JsonNode? json = await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync());
            JsonNode? data = json?["data"];

            if (data?["avatar_list"] == null)
            {
                m_Logger.LogWarning("Failed to deserialize character detail response for user {Uid}", uid);
                return Result<ZzzFullAvatarData>.Failure(HttpStatusCode.InternalServerError,
                    "An error occurred while retrieving character data");
            }

            ZzzFullAvatarData? character = data?.Deserialize<ZzzFullAvatarData>(JsonOptions);

            if (character == null)
            {
                m_Logger.LogWarning("Failed to deserialize character detail response for user {Uid}", uid);
                return Result<ZzzFullAvatarData>.Failure(HttpStatusCode.InternalServerError,
                    "An error occurred while retrieving character data");
            }

            return Result<ZzzFullAvatarData>.Success(character, json?["retcode"]?.GetValue<int>() ?? 0, HttpStatusCode.Accepted);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e,
                "Failed to retrieve character data for user {Uid} on {Region} server (game UID: {GameUid})",
                uid, region, gameUid);
            return Result<ZzzFullAvatarData>.Failure(HttpStatusCode.BadGateway,
                "An error occurred while retrieving character data");
        }
    }

    public async Task<Result<ZzzFullAvatarData>> GetCharacterDataFromNameAsync(ulong uid, string ltoken,
        string gameUid, string region, string characterName)
    {
        try
        {
            m_Logger.LogInformation(
                "Retrieving character data for {CharacterName} for user {Uid} on {Region} server (game UID: {GameUid})",
                characterName, uid, region, gameUid);

            IEnumerable<ZzzBasicAvatarData> characters = await GetAllCharactersAsync(uid, ltoken, gameUid, region);
            ZzzBasicAvatarData? character =
                characters.FirstOrDefault(x => x.Name.Equals(characterName, StringComparison.OrdinalIgnoreCase));

            if (character == null)
            {
                m_Logger.LogWarning("Character {CharacterName} not found for user {Uid}", characterName, uid);
                return Result<ZzzFullAvatarData>.Failure(HttpStatusCode.BadRequest,
                    "Character not found. Please try again");
            }

            Result<ZzzFullAvatarData> result = await GetCharacterDataFromIdAsync(uid, ltoken, gameUid, region, (uint)character.Id!);
            if (!result.IsSuccess)
            {
                m_Logger.LogError("Failed to retrieve data for character {CharacterName} for user {Uid}",
                    characterName, uid);
                return Result<ZzzFullAvatarData>.Failure(result.StatusCode,
                    "An error occurred while retrieving character data");
            }

            return Result<ZzzFullAvatarData>.Success(result.Data);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e,
                "Failed to retrieve character data for user {Uid} on {Region} server (game UID: {GameUid})",
                uid, region, gameUid);
            return Result<ZzzFullAvatarData>.Failure(HttpStatusCode.BadGateway,
                "An error occurred while retrieving character data");
        }
    }
}
