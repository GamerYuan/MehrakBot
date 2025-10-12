using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Zzz.Types;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mehrak.GameApi.Zzz;

internal class ZzzCharacterApiService : ICharacterApiService<ZzzBasicAvatarData, ZzzFullAvatarData, CharacterApiContext>
{
    private const string BasePath = "/event/game_record_zzz/api/zzz/avatar";

    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<ZzzCharacterApiService> m_Logger;
    private readonly ICacheService m_Cache;
    private const int CacheExpirationMinutes = 10;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public ZzzCharacterApiService(ICacheService cache,
        IHttpClientFactory httpClientFactory,
        ILogger<ZzzCharacterApiService> logger)
    {
        m_Cache = cache;
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<IEnumerable<ZzzBasicAvatarData>>> GetAllCharactersAsync(CharacterApiContext context)
    {
        if (string.IsNullOrEmpty(context.Region) || string.IsNullOrEmpty(context.GameUid))
        {
            m_Logger.LogError("Region or Game UID is missing for user {Uid}", context.LtUid);
            return Result<IEnumerable<ZzzBasicAvatarData>>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        try
        {
            m_Logger.LogInformation("Retrieving character list for user {Uid} on {Region} server (game UID: {GameUid})",
                context.UserId, context.Region, context.GameUid);
            string cacheKey = $"zzz_characters_{context.GameUid}";
            var cachedEntry = await m_Cache.GetAsync<IEnumerable<ZzzBasicAvatarData>>(cacheKey);

            if (cachedEntry != null)
            {
                m_Logger.LogInformation("Retrieved character data from cache for game UID: {GameUid}", context.GameUid);
                return Result<IEnumerable<ZzzBasicAvatarData>>.Success(cachedEntry)!;
            }

            HttpClient httpClient = m_HttpClientFactory.CreateClient("Default");

            HttpRequestMessage request = new();
            request.Headers.Add("Cookie", $"ltuid_v2={context.LtUid}; ltoken_v2={context.LToken}");
            request.Headers.Add("X-Rpc-Language", "en-us");
            request.RequestUri = new Uri($"{HoYoLabDomains.PublicApi}{BasePath}/basic?server={context.Region}&role_id={context.GameUid}");

            m_Logger.LogDebug("Sending character list request to {Endpoint}", request.RequestUri);
            HttpResponseMessage response = await httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
                m_Logger.LogWarning("Character list API returned non-success status code: {StatusCode}",
                    response.StatusCode);

            ApiResponse<ZzzBasicAvatarResponse>? json =
                await JsonSerializer.DeserializeAsync<ApiResponse<ZzzBasicAvatarResponse>>(
                    await response.Content.ReadAsStreamAsync(), JsonOptions);

            if (json?.Data == null || json.Data.AvatarList.Count == 0)
            {
                m_Logger.LogError("Failed to deserialize character list response for user {UserId}", context.UserId);
                return Result<IEnumerable<ZzzBasicAvatarData>>.Failure(StatusCode.ExternalServerError,
                    "Failed to retrieve character list data");
            }

            m_Logger.LogInformation("Successfully retrieved {CharacterCount} characters for user {UserId}",
                json.Data.AvatarList.Count, context.UserId);

            var cacheEntry = new CharacterListCacheEntry<ZzzBasicAvatarData>(cacheKey,
                json.Data.AvatarList, TimeSpan.FromMinutes(CacheExpirationMinutes));

            await m_Cache.SetAsync(cacheEntry);
            m_Logger.LogInformation("Cached character data for game UID: {GameUid} for {Minutes} minutes",
                context.GameUid, CacheExpirationMinutes);

            return Result<IEnumerable<ZzzBasicAvatarData>>.Success(json.Data.AvatarList);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Failed to retrieve character list for user {UserId} on {Region} server (game UID: {GameUid})",
                context.UserId, context.Region, context.GameUid);
            return Result<IEnumerable<ZzzBasicAvatarData>>.Failure(StatusCode.BotError,
                "An error occurred while retrieving character data");
        }
    }

    public async Task<Result<ZzzFullAvatarData>> GetCharacterDetailAsync(CharacterApiContext context)
    {
        if (string.IsNullOrEmpty(context.Region) || string.IsNullOrEmpty(context.GameUid) || context.CharacterId == 0)
        {
            m_Logger.LogError("Region or Game UID is missing for user {Uid}", context.LtUid);
            return Result<ZzzFullAvatarData>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        try
        {
            m_Logger.LogInformation(
                "Retrieving character data for {CharacterId} for user {UserId} on {Region} server (game UID: {GameUid})",
                context.CharacterId, context.UserId, context.Region, context.GameUid);

            HttpClient httpClient = m_HttpClientFactory.CreateClient("Default");

            HttpRequestMessage request = new();
            request.Headers.Add("Cookie", $"ltuid_v2={context.LtUid}; ltoken_v2={context.LToken}");
            request.Headers.Add("X-Rpc-Language", "en-us");
            request.RequestUri = new Uri($"{HoYoLabDomains.PublicApi}{BasePath}" +
                $"/info?id_list[]={context.CharacterId}&server={context.Region}&role_id={context.GameUid}&need_wiki=true");
            HttpResponseMessage response = await httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogWarning("Character detail API returned non-success status code: {StatusCode}",
                    response.StatusCode);
                return Result<ZzzFullAvatarData>.Failure(StatusCode.ExternalServerError,
                    "Failed to retrieve character data");
            }

            ApiResponse<ZzzFullAvatarData>? json =
                await JsonSerializer.DeserializeAsync<ApiResponse<ZzzFullAvatarData>>(
                    await response.Content.ReadAsStreamAsync(), JsonOptions);

            if (json?.Data == null || json.Data.AvatarList.Count == 0)
            {
                m_Logger.LogWarning("Failed to deserialize character detail response for user {UserId}", context.UserId);
                return Result<ZzzFullAvatarData>.Failure(StatusCode.ExternalServerError,
                    "Failed to retrieve character data");
            }

            return Result<ZzzFullAvatarData>.Success(json.Data);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e,
                "Failed to retrieve character data for user {Uid} on {Region} server (game UID: {GameUid})",
                context.UserId, context.Region, context.GameUid);
            return Result<ZzzFullAvatarData>.Failure(StatusCode.BotError,
                "An error occurred while retrieving character data");
        }
    }
}
