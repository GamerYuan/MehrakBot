#region

using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mehrak.Domain.Cache;
using Mehrak.Domain.Character;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.GameApi.Genshin.Types;
using Mehrak.GameApi.Shared;
using Mehrak.GameApi.Shared.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.Genshin;

public class GenshinCharacterApiService : ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail,
    GenshinCharacterApiContext>
{
    private static readonly string BasePath = "/event/game_record/genshin/api";

    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<GenshinCharacterApiService> m_Logger;
    private readonly ICacheService m_Cache;
    private const int CacheExpirationMinutes = 10;
    private const int CharacterDetailCacheMinutes = 2;
    private const int WikiCacheMinutes = 30;

    public GenshinCharacterApiService(ICacheService cache,
        IHttpClientFactory httpClientFactory,
        ILogger<GenshinCharacterApiService> logger)
    {
        m_Cache = cache;
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<IEnumerable<GenshinBasicCharacterData>>> GetAllCharactersAsync(GenshinCharacterApiContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(context.Region) || string.IsNullOrEmpty(context.GameUid))
        {
            m_Logger.LogError(LogMessages.InvalidRegionOrUid);
            return Result<IEnumerable<GenshinBasicCharacterData>>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(IApiService.MaxTimeoutSeconds));

            var cacheKey = $"genshin_characters_{context.GameUid}";
            var cachedEntry = await m_Cache.GetAsync<IEnumerable<GenshinBasicCharacterData>>(cacheKey, timeoutCts.Token);

            if (cachedEntry != null)
            {
                return Result<IEnumerable<GenshinBasicCharacterData>>.Success(cachedEntry);
            }

            var requestUri = $"{HoYoLabDomains.PublicApi}{BasePath}/character/list";

            m_Logger.LogInformation(LogMessages.PreparingRequest, requestUri);

            var payload = new CharacterListPayload
            {
                RoleId = context.GameUid,
                Server = context.Region,
                SortType = 1
            };
            var httpClient = m_HttpClientFactory.CreateClient("Default");

            HttpRequestMessage request = new()
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri(requestUri),
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Cookie", $"ltuid_v2={context.LtUid}; ltoken_v2={context.LToken}");
            request.Headers.Add("X-Rpc-Language", "en-us");

            var response = await httpClient.SendAsync(request, timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError(LogMessages.NonSuccessStatusCode, response.StatusCode, requestUri);
                return Result<IEnumerable<GenshinBasicCharacterData>>.Failure(StatusCode.ExternalServerError,
                    "Failed to retrieve character list data", requestUri);
            }

            var json = await response.Content.ReadFromJsonAsync<ApiResponse<CharacterListData>>(timeoutCts.Token);

            if (json?.Data == null || json.Data.List.Count == 0)
            {
                m_Logger.LogError(LogMessages.EmptyResponseData, requestUri, context.UserId);
                return Result<IEnumerable<GenshinBasicCharacterData>>.Failure(StatusCode.ExternalServerError,
                    "Failed to retrieve character list data", requestUri);
            }

            // Info-level API retcode after parse
            m_Logger.LogInformation(LogMessages.InboundHttpResponseWithRetcode, (int)response.StatusCode, requestUri, json.Retcode, context.UserId);

            if (json.Retcode == 10001)
            {
                m_Logger.LogError(LogMessages.InvalidCredentials, context.UserId);
                return Result<IEnumerable<GenshinBasicCharacterData>>.Failure(StatusCode.Unauthorized,
                    "Invalid HoYoLAB UID or Cookies. Please authenticate again.", requestUri);
            }

            if (json.Retcode != 0)
            {
                m_Logger.LogError(LogMessages.UnknownRetcode, json.Retcode, context.UserId, requestUri, json);
                return Result<IEnumerable<GenshinBasicCharacterData>>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later", requestUri);
            }

            await m_Cache.SetAsync(
                new CharacterListCacheEntry<GenshinBasicCharacterData>(cacheKey, json.Data.List,
                    TimeSpan.FromMinutes(CacheExpirationMinutes)), cancellationToken);

            return Result<IEnumerable<GenshinBasicCharacterData>>.Success(json.Data.List, requestUri: requestUri);
        }
        catch (OperationCanceledException)
        {
            return Result<IEnumerable<GenshinBasicCharacterData>>.FromCancellation(cancellationToken);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, LogMessages.ExceptionOccurred,
                $"{HoYoLabDomains.PublicApi}{BasePath}/character/list", context.UserId);
            return Result<IEnumerable<GenshinBasicCharacterData>>.Failure(StatusCode.BotError,
                "An error occurred while retrieving character list data");
        }
    }

    public async Task<Result<GenshinCharacterDetail>> GetCharacterDetailAsync(GenshinCharacterApiContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(context.Region) || string.IsNullOrEmpty(context.GameUid) || context.CharacterIds.Count == 0)
        {
            m_Logger.LogError(LogMessages.InvalidRegionOrUid);
            return Result<GenshinCharacterDetail>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(IApiService.MaxTimeoutSeconds));

            // Atomic per-entry cache lookups
            var requestedIds = context.CharacterIds.Distinct().ToList();
            var cacheKeys = requestedIds.ToDictionary(
                id => id,
                id => $"genshin_char:{context.GameUid}:{id}");

            var cacheTasks = requestedIds
                .Select(id => m_Cache.GetAsync<GenshinCharacterInformation>(cacheKeys[id], timeoutCts.Token))
                .ToList();

            await Task.WhenAll(cacheTasks);

            var cachedEntries = new List<GenshinCharacterInformation>();
            var uncachedIds = new List<int>();

            for (int i = 0; i < requestedIds.Count; i++)
            {
                if (cacheTasks[i].Result != null)
                    cachedEntries.Add(cacheTasks[i].Result!);
                else
                    uncachedIds.Add(requestedIds[i]);
            }

            if (uncachedIds.Count == 0)
            {
                // All characters cached — load wiki entries from cache
                var avatarWiki = await LoadCachedWiki(cachedEntries.Select(c => c.Base.Id).ToList(),
                    "genshin_avatar_wiki", timeoutCts.Token);

                var weaponIds = cachedEntries.Select(c => c.Weapon.Id)
                    .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
                var weaponWiki = await LoadCachedWiki(weaponIds, "genshin_weapon_wiki", timeoutCts.Token);

                return Result<GenshinCharacterDetail>.Success(new GenshinCharacterDetail
                {
                    List = cachedEntries,
                    AvatarWiki = avatarWiki,
                    WeaponWiki = weaponWiki
                });
            }

            // Fetch only uncached IDs from API
            var requestUri = $"{HoYoLabDomains.PublicApi}{BasePath}/character/detail";

            m_Logger.LogInformation(LogMessages.PreparingRequest, requestUri);

            var payload = new CharacterDetailPayload
            {
                RoleId = context.GameUid,
                Server = context.Region,
                CharacterIds = [.. uncachedIds]
            };
            var httpClient = m_HttpClientFactory.CreateClient("Default");

            HttpRequestMessage request = new()
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri(requestUri),
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Cookie", $"ltuid_v2={context.LtUid}; ltoken_v2={context.LToken}");
            request.Headers.Add("X-Rpc-Language", "en-us");

            var response = await httpClient.SendAsync(request, timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError(LogMessages.NonSuccessStatusCode, response.StatusCode, requestUri);
                return Result<GenshinCharacterDetail>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while retrieving character data", requestUri);
            }

            var json =
                await response.Content.ReadFromJsonAsync<ApiResponse<GenshinCharacterDetail>>(timeoutCts.Token);

            if (json == null || json.Data == null || json.Data.List.Count == 0)
            {
                m_Logger.LogError(LogMessages.EmptyResponseData, requestUri, context.UserId);
                return Result<GenshinCharacterDetail>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while retrieving character data", requestUri);
            }

            // Info-level API retcode after parse
            m_Logger.LogInformation(LogMessages.InboundHttpResponseWithRetcode, (int)response.StatusCode, requestUri, json.Retcode, context.UserId);

            if (json?.Retcode == 10001)
            {
                m_Logger.LogError(LogMessages.InvalidCredentials, context.UserId);
                return Result<GenshinCharacterDetail>.Failure(StatusCode.Unauthorized,
                    "Invalid HoYoLAB UID or Cookies. Please authenticate again.", requestUri);
            }

            if (json?.Retcode != 0)
            {
                m_Logger.LogError(LogMessages.UnknownRetcode, json?.Retcode, context.UserId, requestUri, json);
                return Result<GenshinCharacterDetail>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later");
            }

            // Cache each new entry individually
            var cacheWriteTasks = new List<Task>();

            foreach (var charInfo in json.Data.List)
            {
                cacheWriteTasks.Add(m_Cache.SetAsync(
                    new CacheEntryBase<GenshinCharacterInformation>(
                        $"genshin_char:{context.GameUid}:{charInfo.Base.Id}",
                        charInfo,
                        TimeSpan.FromMinutes(CharacterDetailCacheMinutes)),
                    cancellationToken));
            }

            foreach (var kvp in json.Data.AvatarWiki)
            {
                cacheWriteTasks.Add(m_Cache.SetAsync(
                    new CacheEntryBase<string>(
                        $"genshin_avatar_wiki:{kvp.Key}",
                        kvp.Value,
                        TimeSpan.FromMinutes(WikiCacheMinutes)),
                    cancellationToken));
            }

            foreach (var kvp in json.Data.WeaponWiki)
            {
                cacheWriteTasks.Add(m_Cache.SetAsync(
                    new CacheEntryBase<string>(
                        $"genshin_weapon_wiki:{kvp.Key}",
                        kvp.Value,
                        TimeSpan.FromMinutes(WikiCacheMinutes)),
                    cancellationToken));
            }

            await Task.WhenAll(cacheWriteTasks);

            // Load cached wiki entries for already-cached characters
            var cachedAvatarWiki = await LoadCachedWiki(cachedEntries.Select(c => c.Base.Id).ToList(),
                "genshin_avatar_wiki", timeoutCts.Token);

            var cachedWeaponIds = cachedEntries.Select(c => c.Weapon.Id)
                .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
            var cachedWeaponWiki = await LoadCachedWiki(cachedWeaponIds, "genshin_weapon_wiki", timeoutCts.Token);

            // Merge cached entries with API response
            var mergedById = cachedEntries.Concat(json.Data.List)
                .ToDictionary(c => c.Base.Id);
            var mergedList = requestedIds
                .Where(mergedById.ContainsKey)
                .Select(id => mergedById[id])
                .ToList();

            var mergedAvatarWiki = new Dictionary<string, string>(cachedAvatarWiki);
            foreach (var kvp in json.Data.AvatarWiki)
                mergedAvatarWiki[kvp.Key] = kvp.Value;

            var mergedWeaponWiki = new Dictionary<string, string>(cachedWeaponWiki);
            foreach (var kvp in json.Data.WeaponWiki)
                mergedWeaponWiki[kvp.Key] = kvp.Value;

            return Result<GenshinCharacterDetail>.Success(new GenshinCharacterDetail
            {
                List = mergedList,
                AvatarWiki = mergedAvatarWiki,
                WeaponWiki = mergedWeaponWiki
            }, requestUri: requestUri);
        }
        catch (OperationCanceledException)
        {
            return Result<GenshinCharacterDetail>.FromCancellation(cancellationToken);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, LogMessages.ExceptionOccurred,
                $"{HoYoLabDomains.PublicApi}{BasePath}/character/detail", context.UserId);
            return Result<GenshinCharacterDetail>.Failure(StatusCode.BotError,
                "An error occurred while retrieving character data");
        }
    }

    private async Task<Dictionary<string, string>> LoadCachedWiki(IReadOnlyList<int> ids, string prefix, CancellationToken cancellationToken)
    {
        var tasks = ids.Select(id => m_Cache.GetAsync<string>($"{prefix}:{id}", cancellationToken)).ToList();
        await Task.WhenAll(tasks);

        var result = new Dictionary<string, string>();
        for (int i = 0; i < ids.Count; i++)
        {
            if (tasks[i].Result != null)
                result[ids[i].ToString()] = tasks[i].Result!;
        }
        return result;
    }

    private sealed class CharacterListPayload
    {
        [JsonPropertyName("role_id")] public string RoleId { get; set; } = string.Empty;

        [JsonPropertyName("server")] public string Server { get; set; } = string.Empty;

        [JsonPropertyName("sort_type")] public int SortType { get; set; }
    }

    private sealed class CharacterDetailPayload
    {
        [JsonPropertyName("role_id")] public string RoleId { get; set; } = string.Empty;

        [JsonPropertyName("server")] public string Server { get; set; } = string.Empty;

        [JsonPropertyName("character_ids")] public List<int> CharacterIds { get; set; } = [];
    }
}
