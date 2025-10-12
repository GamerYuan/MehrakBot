#region

using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Genshin.Types;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

#endregion

namespace Mehrak.GameApi.Genshin;

public class GenshinCharacterApiService : ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, CharacterApiContext>
{
    private static readonly string BasePath = "/event/game_record/genshin/api";

    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<GenshinCharacterApiService> m_Logger;
    private readonly ICacheService m_Cache;
    private const int CacheExpirationMinutes = 10;

    public GenshinCharacterApiService(ICacheService cache,
        IHttpClientFactory httpClientFactory,
        ILogger<GenshinCharacterApiService> logger)
    {
        m_Cache = cache;
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<IEnumerable<GenshinBasicCharacterData>>> GetAllCharactersAsync(CharacterApiContext context)
    {
        if (string.IsNullOrEmpty(context.Region) || string.IsNullOrEmpty(context.GameUid))
        {
            m_Logger.LogError("Region or Game UID is missing for user {Uid}", context.LtUid);
            return Result<IEnumerable<GenshinBasicCharacterData>>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        try
        {
            m_Logger.LogInformation("Retrieving character list for user {Uid} on {Region} server (game UID: {GameUid})",
                context.UserId, context.Region, context.GameUid);
            string cacheKey = $"genshin_characters_{context.GameUid}";

            var cachedEntry = await m_Cache.GetAsync<IEnumerable<GenshinBasicCharacterData>>(cacheKey);

            if (cachedEntry != null)
            {
                m_Logger.LogInformation("Retrieved character data from cache for game UID: {GameUid}", context.GameUid);
                return Result<IEnumerable<GenshinBasicCharacterData>>.Success(cachedEntry);
            }

            var payload = new CharacterListPayload()
            {
                RoleId = context.GameUid,
                Server = context.Region,
                SortType = 1
            };
            var httpClient = m_HttpClientFactory.CreateClient("Default");

            HttpRequestMessage request = new()
            {
                Method = HttpMethod.Post
            };
            request.Headers.Add("Cookie", $"ltuid_v2={context.LtUid}; ltoken_v2={context.LToken}");
            request.Headers.Add("X-Rpc-Language", "en-us");
            request.RequestUri = new Uri($"{HoYoLabDomains.PublicApi}{BasePath}/character/list");
            request.Content =
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            m_Logger.LogDebug("Sending character list request to {Endpoint}", request.RequestUri);
            var response = await httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
                m_Logger.LogWarning("Character list API returned non-success status code: {StatusCode}",
                    response.StatusCode);

            var json = await response.Content.ReadFromJsonAsync<ApiResponse<CharacterListData>>();

            if (json?.Data == null || json.Data.List.Count == 0)
            {
                m_Logger.LogError("Failed to deserialize character list response for user {UserId}", context.UserId);
                return Result<IEnumerable<GenshinBasicCharacterData>>.Failure(StatusCode.ExternalServerError,
                    "Failed to retrieve character list data");
            }

            m_Logger.LogInformation("Successfully retrieved {CharacterCount} characters for user {Uid}",
                json.Data.List.Count, context.LtUid);

            await m_Cache.SetAsync(
                new CharacterListCacheEntry<GenshinBasicCharacterData>(cacheKey, json.Data.List, TimeSpan.FromMinutes(CacheExpirationMinutes)));
            m_Logger.LogInformation("Cached character data for game UID: {GameUid} for {Minutes} minutes",
                context.GameUid, CacheExpirationMinutes);

            return Result<IEnumerable<GenshinBasicCharacterData>>.Success(json.Data.List);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e,
                "Failed to retrieve character list for user {Uid} on {Region} server (game UID: {GameUid})",
                context.UserId, context.Region, context.GameUid);
            return Result<IEnumerable<GenshinBasicCharacterData>>.Failure(StatusCode.BotError,
                "An error occurred while retrieving character list data");
        }
    }

    public async Task<Result<GenshinCharacterDetail>> GetCharacterDetailAsync(CharacterApiContext context)
    {
        if (string.IsNullOrEmpty(context.Region) || string.IsNullOrEmpty(context.GameUid) || context.CharacterId == 0)
        {
            m_Logger.LogError("Region or Game UID is missing for user {Uid}", context.LtUid);
            return Result<GenshinCharacterDetail>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        try
        {
            m_Logger.LogInformation(
                "Retrieving character data for {CharacterId} for user {UserId} on {Region} server (game UID: {GameUid})",
                context.CharacterId, context.UserId, context.Region, context.GameUid);

            var payload = new CharacterDetailPayload()
            {
                RoleId = context.GameUid,
                Server = context.Region,
                CharacterIds = [context.CharacterId]
            };
            var httpClient = m_HttpClientFactory.CreateClient("Default");

            HttpRequestMessage request = new()
            {
                Method = HttpMethod.Post
            };
            request.Headers.Add("Cookie", $"ltuid_v2={context.LtUid}; ltoken_v2={context.LToken}");
            request.Headers.Add("X-Rpc-Language", "en-us");
            request.RequestUri = new Uri($"{HoYoLabDomains.PublicApi}{BasePath}/character/detail");
            request.Content =
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            m_Logger.LogDebug("Sending character detail request to {Endpoint}", request.RequestUri);
            var response = await httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogWarning("Character detail API returned non-success status code: {StatusCode}",
                    response.StatusCode);
                return Result<GenshinCharacterDetail>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while retrieving character data");
            }

            ApiResponse<GenshinCharacterDetail>? json = await response.Content.ReadFromJsonAsync<ApiResponse<GenshinCharacterDetail>>();

            if (json?.Data == null || json?.Data.List.Count == 0)
            {
                m_Logger.LogWarning("Failed to deserialize character detail response for user {UserId}", context.UserId);
                return Result<GenshinCharacterDetail>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while retrieving character data");
            }

            return Result<GenshinCharacterDetail>.Success(json!.Data);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e,
                "Failed to retrieve character data for user {UserId} on {Region} server (game UID: {GameUid})",
                context.UserId, context.Region, context.GameUid);
            return Result<GenshinCharacterDetail>.Failure(StatusCode.BotError,
                "An error occurred while retrieving character data");
        }
    }

    private sealed class CharacterListPayload
    {
        [JsonPropertyName("role_id")]
        public string RoleId { get; set; } = string.Empty;

        [JsonPropertyName("server")] public string Server { get; set; } = string.Empty;

        [JsonPropertyName("sort_type")]
        public int SortType { get; set; }
    }

    private sealed class CharacterDetailPayload
    {
        [JsonPropertyName("role_id")]
        public string RoleId { get; set; } = string.Empty;

        [JsonPropertyName("server")] public string Server { get; set; } = string.Empty;

        [JsonPropertyName("character_ids")]
        public List<int> CharacterIds { get; set; } = [];
    }
}
