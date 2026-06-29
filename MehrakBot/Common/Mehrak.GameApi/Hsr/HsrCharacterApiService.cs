#region

using System.Text.Json;
using Mehrak.Domain.Cache;
using Mehrak.Domain.Character;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.GameApi.Hsr.Types;
using Mehrak.GameApi.Shared;
using Mehrak.GameApi.Shared.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.Hsr;

public class
    HsrCharacterApiService : ICharacterApiService<HsrBasicCharacterData, HsrBasicCharacterData, CharacterApiContext>
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<HsrCharacterApiService> m_Logger;
    private readonly ICacheService m_Cache;

    private static readonly string ApiEndpoint = "/event/game_record/hkrpg/api/avatar/info";
    private const int CacheExpirationMinutes = 10;
    private const int CharacterDetailCacheMinutes = 2;
    private const int WikiCacheMinutes = 30;

    public HsrCharacterApiService(IHttpClientFactory httpClientFactory, ICacheService cache,
        ILogger<HsrCharacterApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
        m_Cache = cache;
    }

    public async Task<Result<IEnumerable<HsrBasicCharacterData>>> GetAllCharactersAsync(CharacterApiContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(context.Region) || string.IsNullOrEmpty(context.GameUid))
        {
            m_Logger.LogError(LogMessages.InvalidRegionOrUid);
            return Result<IEnumerable<HsrBasicCharacterData>>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(IApiService.MaxTimeoutSeconds));

            var cacheKey = $"hsr_characters_{context.GameUid}";
            var cachedData = await m_Cache.GetAsync<IEnumerable<HsrBasicCharacterData>>(cacheKey, timeoutCts.Token);

            // Try to get data from cache first
            if (cachedData != null)
            {
                return Result<IEnumerable<HsrBasicCharacterData>>.Success(cachedData);
            }

            var requestUri =
                $"{HoYoLabDomains.PublicApi}{ApiEndpoint}?server={context.Region}&role_id={context.GameUid}&need_wiki=true";

            m_Logger.LogInformation(LogMessages.PreparingRequest, requestUri);

            var client = m_HttpClientFactory.CreateClient("Default");
            using HttpRequestMessage request = new()
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri(requestUri),
                Headers =
                {
                    { "Cookie", $"ltuid_v2={context.LtUid}; ltoken_v2={context.LToken}" },
                    { "X-Rpc-Client_type", "5" },
                    { "X-Rpc-App_version", "1.5.0" },
                    { "X-Rpc-Language", "en-us" },
                    { "DS", DSGenerator.GenerateDS() }
                }
            };

            using var response = await client.SendAsync(request, timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError(LogMessages.NonSuccessStatusCode, response.StatusCode, requestUri);
                return Result<IEnumerable<HsrBasicCharacterData>>.Failure(StatusCode.ExternalServerError,
                    "Failed to retrieve character information", requestUri);
            }

            var json =
                await JsonSerializer.DeserializeAsync<ApiResponse<HsrBasicCharacterData>>(
                    await response.Content.ReadAsStreamAsync(timeoutCts.Token), (JsonSerializerOptions?)null, timeoutCts.Token);

            if (json?.Data == null || json.Data.AvatarList.Count == 0)
            {
                m_Logger.LogError(LogMessages.EmptyResponseData, requestUri, context.UserId);
                return Result<IEnumerable<HsrBasicCharacterData>>.Failure(StatusCode.ExternalServerError,
                    "Failed to retrieve character information", requestUri);
            }

            // Info-level API retcode after parse
            m_Logger.LogInformation(LogMessages.InboundHttpResponseWithRetcode, (int)response.StatusCode, requestUri, json.Retcode, context.UserId);

            if (json.Retcode == 10001)
            {
                m_Logger.LogError(LogMessages.InvalidCredentials, context.UserId);
                return Result<IEnumerable<HsrBasicCharacterData>>.Failure(StatusCode.Unauthorized,
                    "Invalid HoYoLAB UID or Cookies. Please authenticate again.", requestUri);
            }

            if (json.Retcode != 0)
            {
                m_Logger.LogError(LogMessages.UnknownRetcode, json.Retcode, context.UserId, requestUri, json);
                return Result<IEnumerable<HsrBasicCharacterData>>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later", requestUri);
            }

            HsrBasicCharacterData[] result = [json.Data];

            await m_Cache.SetAsync(
                new CharacterListCacheEntry<HsrBasicCharacterData>(cacheKey, result,
                    TimeSpan.FromMinutes(CacheExpirationMinutes)), cancellationToken);

            return Result<IEnumerable<HsrBasicCharacterData>>.Success(result, requestUri: requestUri);
        }
        catch (OperationCanceledException)
        {
            return Result<IEnumerable<HsrBasicCharacterData>>.FromCancellation(cancellationToken);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, LogMessages.ExceptionOccurred,
                $"{HoYoLabDomains.PublicApi}{ApiEndpoint}", context.UserId);
            return Result<IEnumerable<HsrBasicCharacterData>>.Failure(StatusCode.BotError,
                "An error occurred while retrieving character information");
        }
    }

    public async Task<Result<HsrBasicCharacterData>> GetCharacterDetailAsync(CharacterApiContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(context.Region) || string.IsNullOrEmpty(context.GameUid) || context.CharacterId == 0)
        {
            m_Logger.LogError(LogMessages.InvalidRegionOrUid);
            return Result<HsrBasicCharacterData>.Failure(StatusCode.BadParameter,
                "Game UID, region, or character ID is invalid");
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(IApiService.MaxTimeoutSeconds));

            // Check cache for the requested character
            var charCacheKey = $"hsr_char:{context.GameUid}:{context.CharacterId}";
            var cachedChar = await m_Cache.GetAsync<HsrCharacterInformation>(charCacheKey, timeoutCts.Token);

            if (cachedChar != null)
            {
                // Character cached — load wiki entries from cache if available
                var equipWiki = new Dictionary<string, string>();
                if (cachedChar.Equip != null)
                {
                    var equipKey = $"hsr_equip_wiki:{cachedChar.Equip.Id}";
                    var cachedEquip = await m_Cache.GetAsync<string>(equipKey, timeoutCts.Token);
                    if (cachedEquip != null)
                        equipWiki[cachedChar.Equip.Id.ToString()] = cachedEquip;
                }

                var relicWiki = new Dictionary<string, string>();
                var relicIds = cachedChar.Relics.Select(r => r.Id)
                    .Concat(cachedChar.Ornaments.Select(r => r.Id))
                    .Distinct().ToArray();

                var relicTasks = relicIds.Select(id =>
                    m_Cache.GetAsync<string>($"hsr_relic_wiki:{id}", timeoutCts.Token));
                var relicResults = await Task.WhenAll(relicTasks);

                for (var i = 0; i < relicIds.Length; i++)
                {
                    if (relicResults[i] != null)
                        relicWiki[relicIds[i].ToString()] = relicResults[i]!;
                }

                return Result<HsrBasicCharacterData>.Success(new HsrBasicCharacterData
                {
                    AvatarList = [cachedChar],
                    EquipWiki = equipWiki,
                    RelicWiki = relicWiki
                });
            }

            // Cache miss — fetch all from API
            var requestUri =
                $"{HoYoLabDomains.PublicApi}{ApiEndpoint}?server={context.Region}&role_id={context.GameUid}&need_wiki=true";

            m_Logger.LogInformation(LogMessages.PreparingRequest, requestUri);

            var client = m_HttpClientFactory.CreateClient("Default");
            using HttpRequestMessage request = new()
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri(requestUri),
                Headers =
                {
                    { "Cookie", $"ltuid_v2={context.LtUid}; ltoken_v2={context.LToken}" },
                    { "X-Rpc-Client_type", "5" },
                    { "X-Rpc-App_version", "1.5.0" },
                    { "X-Rpc-Language", "en-us" },
                    { "DS", DSGenerator.GenerateDS() }
                }
            };

            using var response = await client.SendAsync(request, timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError(LogMessages.NonSuccessStatusCode, response.StatusCode, requestUri);
                return Result<HsrBasicCharacterData>.Failure(StatusCode.ExternalServerError,
                    "Failed to retrieve character information", requestUri);
            }

            var json =
                await JsonSerializer.DeserializeAsync<ApiResponse<HsrBasicCharacterData>>(
                    await response.Content.ReadAsStreamAsync(timeoutCts.Token), (JsonSerializerOptions?)null, timeoutCts.Token);

            if (json?.Data == null || json.Data.AvatarList.Count == 0)
            {
                m_Logger.LogError(LogMessages.EmptyResponseData, requestUri, context.UserId);
                return Result<HsrBasicCharacterData>.Failure(StatusCode.ExternalServerError,
                    "Failed to retrieve character information", requestUri);
            }

            m_Logger.LogInformation(LogMessages.InboundHttpResponseWithRetcode, (int)response.StatusCode, requestUri, json.Retcode, context.UserId);

            if (json.Retcode == 10001)
            {
                m_Logger.LogError(LogMessages.InvalidCredentials, context.UserId);
                return Result<HsrBasicCharacterData>.Failure(StatusCode.Unauthorized,
                    "Invalid HoYoLAB UID or Cookies. Please authenticate again.", requestUri);
            }

            if (json.Retcode != 0)
            {
                m_Logger.LogError(LogMessages.UnknownRetcode, json.Retcode, context.UserId, requestUri, json);
                return Result<HsrBasicCharacterData>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later", requestUri);
            }

            // Cache each character individually
            var charCacheTasks = json.Data.AvatarList.Select(avatar =>
                m_Cache.SetAsync(
                    new CacheEntryBase<HsrCharacterInformation>(
                        $"hsr_char:{context.GameUid}:{avatar.Id}",
                        avatar,
                        TimeSpan.FromMinutes(CharacterDetailCacheMinutes)),
                    cancellationToken));
            await Task.WhenAll(charCacheTasks);

            // Cache equip wiki entries
            var equipWikiCacheTasks = json.Data.EquipWiki.Select(kvp =>
                m_Cache.SetAsync(
                    new CacheEntryBase<string>(
                        $"hsr_equip_wiki:{kvp.Key}",
                        kvp.Value,
                        TimeSpan.FromMinutes(WikiCacheMinutes)),
                    cancellationToken));
            await Task.WhenAll(equipWikiCacheTasks);

            // Cache relic wiki entries
            var relicWikiCacheTasks = json.Data.RelicWiki.Select(kvp =>
                m_Cache.SetAsync(
                    new CacheEntryBase<string>(
                        $"hsr_relic_wiki:{kvp.Key}",
                        kvp.Value,
                        TimeSpan.FromMinutes(WikiCacheMinutes)),
                    cancellationToken));
            await Task.WhenAll(relicWikiCacheTasks);

            // Find and return the requested character
            var requestedChar = json.Data.AvatarList.FirstOrDefault(x => x.Id == context.CharacterId);
            if (requestedChar == null)
            {
                m_Logger.LogError("Requested character {CharacterId} not found in API response", context.CharacterId);
                return Result<HsrBasicCharacterData>.Failure(StatusCode.ExternalServerError,
                    "Requested character not found", requestUri);
            }

            return Result<HsrBasicCharacterData>.Success(new HsrBasicCharacterData
            {
                AvatarList = [requestedChar],
                EquipWiki = json.Data.EquipWiki,
                RelicWiki = json.Data.RelicWiki
            }, requestUri: requestUri);
        }
        catch (OperationCanceledException)
        {
            return Result<HsrBasicCharacterData>.FromCancellation(cancellationToken);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, LogMessages.ExceptionOccurred,
                $"{HoYoLabDomains.PublicApi}{ApiEndpoint}", context.UserId);
            return Result<HsrBasicCharacterData>.Failure(StatusCode.BotError,
                "An error occurred while retrieving character information");
        }
    }
}
