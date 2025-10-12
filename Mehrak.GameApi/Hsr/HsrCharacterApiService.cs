#region

using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Hsr.Types;
using Mehrak.GameApi.Utilities;
using Microsoft.Extensions.Logging;
using System.Text.Json;

#endregion

namespace Mehrak.GameApi.Hsr;

public class HsrCharacterApiService : ICharacterApiService<HsrBasicCharacterData, HsrCharacterInformation, CharacterApiContext>
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<HsrCharacterApiService> m_Logger;
    private readonly ICacheService m_Cache;

    private static readonly string ApiEndpoint = "/event/game_record/hkrpg/api/avatar/info";
    private const int CacheExpirationMinutes = 10;

    public HsrCharacterApiService(IHttpClientFactory httpClientFactory, ICacheService cache,
        ILogger<HsrCharacterApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
        m_Cache = cache;
    }

    public async Task<Result<IEnumerable<HsrBasicCharacterData>>> GetAllCharactersAsync(CharacterApiContext context)
    {
        if (string.IsNullOrEmpty(context.Region) || string.IsNullOrEmpty(context.GameUid))
        {
            m_Logger.LogError("Region or Game UID is missing for user {Uid}", context.LtUid);
            return Result<IEnumerable<HsrBasicCharacterData>>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        try
        {
            string cacheKey = $"hsr_characters_{context.GameUid}";
            var cachedData = await m_Cache.GetAsync<IEnumerable<HsrBasicCharacterData>>(cacheKey);

            // Try to get data from cache first
            if (cachedData != null)
            {
                m_Logger.LogInformation("Retrieved character data from cache for game UID: {GameUid}", context.GameUid);
                return Result<IEnumerable<HsrBasicCharacterData>>.Success(cachedData);
            }

            HttpClient client = m_HttpClientFactory.CreateClient("Default");
            m_Logger.LogInformation("Retrieving character list for user {UserId} on {Region} server (game UID: {GameUid})",
                context.UserId, context.Region, context.GameUid);
            HttpRequestMessage request = new()
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri($"{HoYoLabDomains.PublicApi}{ApiEndpoint}?server={context.Region}&role_id={context.GameUid}&need_wiki=true"),
                Headers =
                {
                    { "Cookie", $"ltuid_v2={context.LtUid}; ltoken_v2={context.LToken}" },
                    { "X-Rpc-Client_type", "5" },
                    { "X-Rpc-App_version", "1.5.0" },
                    { "X-Rpc-Language", "en-us" },
                    { "DS", DSGenerator.GenerateDS() }
                }
            };
            m_Logger.LogDebug("Sending character list request to {Endpoint}", request.RequestUri);
            HttpResponseMessage response = await client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError("Error sending character list request to {Endpoint}", request.RequestUri);
                return Result<IEnumerable<HsrBasicCharacterData>>.Failure(StatusCode.ExternalServerError,
                    "Failed to retrieve character information");
            }

            ApiResponse<HsrBasicCharacterData>? json = await JsonSerializer.DeserializeAsync<ApiResponse<HsrBasicCharacterData>>(
                await response.Content.ReadAsStreamAsync());

            if (json?.Data == null || json.Data.AvatarList.Count == 0)
            {
                m_Logger.LogWarning("No character data found for user {UserId} on {Region} server (game UID: {GameUid})",
                   context.UserId, context.Region, context.GameUid);
                return Result<IEnumerable<HsrBasicCharacterData>>.Failure(StatusCode.ExternalServerError,
                    "Failed to retrieve character information");
            }

            m_Logger.LogInformation(
                "Successfully retrieved {Count} characters for user {UserId} on {Region} server (game UID: {GameUid})",
                json.Data.AvatarList.Count, context.UserId, context.Region, context.GameUid);

            HsrBasicCharacterData[] result = [json.Data];

            await m_Cache.SetAsync(
                new CharacterListCacheEntry<HsrBasicCharacterData>(cacheKey, result, TimeSpan.FromMinutes(CacheExpirationMinutes)));
            m_Logger.LogInformation("Cached character data for game UID: {GameUid} for {Minutes} minutes",
                context.GameUid, CacheExpirationMinutes);

            return Result<IEnumerable<HsrBasicCharacterData>>.Success(result);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e,
                "An error occurred while retrieving character data for user {UserId} on {Region} server (game UID: {GameUid})",
                context.UserId, context.Region, context.GameUid);
            return Result<IEnumerable<HsrBasicCharacterData>>.Failure(StatusCode.BotError,
                "An error occurred while retrieving character information");
        }
    }

    public Task<Result<HsrCharacterInformation>> GetCharacterDetailAsync(CharacterApiContext context)
    {
        throw new NotImplementedException();
    }
}
