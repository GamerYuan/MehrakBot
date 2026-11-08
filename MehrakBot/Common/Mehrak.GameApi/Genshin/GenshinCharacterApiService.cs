#region

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

        var requestUri = $"{HoYoLabDomains.PublicApi}{BasePath}/character/list";

        try
        {
            using var scope = new HoYoLabRequestScope(cancellationToken);

            var cacheKey = $"genshin_characters_{context.GameUid}";
            var cachedEntry = await m_Cache.GetAsync<IEnumerable<GenshinBasicCharacterData>>(cacheKey, scope.Token);

            if (cachedEntry != null)
            {
                return Result<IEnumerable<GenshinBasicCharacterData>>.Success(cachedEntry);
            }

            var payload = new CharacterListPayload
            {
                RoleId = context.GameUid,
                Server = context.Region,
                SortType = 1
            };

            return await HoYoLabApi.ExecuteAsync(
                new HoYoLabRequest
                {
                    RequestUri = requestUri,
                    Method = HttpMethod.Post,
                    Body = payload,
                    Headers = [new KeyValuePair<string, string>("X-Rpc-Language", "en-us")],
                    Failures = new HoYoLabFailureMessages
                    {
                        Unauthorized = "Invalid HoYoLAB UID or Cookies. Please authenticate again.",
                        Parse = "Failed to retrieve character list data",
                        NonSuccessStatus = "Failed to retrieve character list data"
                    }
                },
                new HoYoLabEnvelopePolicy<CharacterListData>
                {
                    HasData = data => data.List is { Count: > 0 },
                    UnknownRetcode = "An unknown error occurred when accessing HoYoLAB API. Please try again later",
                    EmptyData = "Failed to retrieve character list data"
                },
                new HoYoLabCredentials(context.UserId, context.LtUid, context.LToken),
                m_HttpClientFactory, m_Logger, scope.Token,
                async data =>
                {
                    await m_Cache.SetAsync(
                        new CharacterListCacheEntry<GenshinBasicCharacterData>(cacheKey, data.List,
                            TimeSpan.FromMinutes(CacheExpirationMinutes)), scope.Token);

                    return Result<IEnumerable<GenshinBasicCharacterData>>.Success(data.List, requestUri: requestUri);
                });
        }
        catch (OperationCanceledException)
        {
            return Result<IEnumerable<GenshinBasicCharacterData>>.FromCancellation(cancellationToken, requestUri);
        }
        catch (Exception e)
        {
            GameApiLog.LogException(m_Logger, $"{HoYoLabDomains.PublicApi}{BasePath}/character/list", context.UserId, e);
            return Result<IEnumerable<GenshinBasicCharacterData>>.Failure(StatusCode.BotError,
                "An error occurred while retrieving character list data", requestUri);
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

        string? requestUri = null;

        try
        {
            using var scope = new HoYoLabRequestScope(cancellationToken);

            // Atomic per-entry cache lookups
            var requestedIds = context.CharacterIds.Distinct().ToList();
            var cacheKeys = requestedIds.ToDictionary(
                id => id,
                id => $"genshin_char:{context.GameUid}:{id}");

            var cacheTasks = requestedIds
                .Select(id => m_Cache.GetAsync<GenshinCharacterInformation>(cacheKeys[id], scope.Token))
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
                    "genshin_avatar_wiki", scope.Token);

                var weaponIds = cachedEntries.Select(c => c.Weapon.Id)
                    .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
                var weaponWiki = await LoadCachedWiki(weaponIds, "genshin_weapon_wiki", scope.Token);

                return Result<GenshinCharacterDetail>.Success(new GenshinCharacterDetail
                {
                    List = cachedEntries,
                    AvatarWiki = avatarWiki,
                    WeaponWiki = weaponWiki
                });
            }

            // Fetch only uncached IDs from API
            requestUri = $"{HoYoLabDomains.PublicApi}{BasePath}/character/detail";

            var payload = new CharacterDetailPayload
            {
                RoleId = context.GameUid,
                Server = context.Region,
                CharacterIds = [.. uncachedIds]
            };

            return await HoYoLabApi.ExecuteAsync(
                new HoYoLabRequest
                {
                    RequestUri = requestUri,
                    Method = HttpMethod.Post,
                    Body = payload,
                    Headers = [new KeyValuePair<string, string>("X-Rpc-Language", "en-us")],
                    Failures = new HoYoLabFailureMessages
                    {
                        Unauthorized = "Invalid HoYoLAB UID or Cookies. Please authenticate again.",
                        Parse = "An error occurred while retrieving character data",
                        NonSuccessStatus = "An error occurred while retrieving character data"
                    }
                },
                new HoYoLabEnvelopePolicy<GenshinCharacterDetail>
                {
                    HasData = data => data.List is { Count: > 0 },
                    UnknownRetcode = "An unknown error occurred when accessing HoYoLAB API. Please try again later",
                    EmptyData = "An error occurred while retrieving character data"
                },
                new HoYoLabCredentials(context.UserId, context.LtUid, context.LToken),
                m_HttpClientFactory, m_Logger, scope.Token,
                async data =>
                {
                    // Cache each new entry individually
                    var cacheWriteTasks = new List<Task>();

                    foreach (var charInfo in data.List)
                    {
                        cacheWriteTasks.Add(m_Cache.SetAsync(
                            new CacheEntryBase<GenshinCharacterInformation>(
                                $"genshin_char:{context.GameUid}:{charInfo.Base.Id}",
                                charInfo,
                                TimeSpan.FromMinutes(CharacterDetailCacheMinutes)),
                            scope.Token));
                    }

                    foreach (var kvp in data.AvatarWiki)
                    {
                        cacheWriteTasks.Add(m_Cache.SetAsync(
                            new CacheEntryBase<string>(
                                $"genshin_avatar_wiki:{kvp.Key}",
                                kvp.Value,
                                TimeSpan.FromMinutes(WikiCacheMinutes)),
                            scope.Token));
                    }

                    foreach (var kvp in data.WeaponWiki)
                    {
                        cacheWriteTasks.Add(m_Cache.SetAsync(
                            new CacheEntryBase<string>(
                                $"genshin_weapon_wiki:{kvp.Key}",
                                kvp.Value,
                                TimeSpan.FromMinutes(WikiCacheMinutes)),
                            scope.Token));
                    }

                    await Task.WhenAll(cacheWriteTasks);

                    // Load cached wiki entries for already-cached characters
                    var cachedAvatarWiki = await LoadCachedWiki(cachedEntries.Select(c => c.Base.Id).ToList(),
                        "genshin_avatar_wiki", scope.Token);

                    var cachedWeaponIds = cachedEntries.Select(c => c.Weapon.Id)
                        .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
                    var cachedWeaponWiki = await LoadCachedWiki(cachedWeaponIds, "genshin_weapon_wiki", scope.Token);

                    // Merge cached entries with API response
                    var mergedById = cachedEntries.Concat(data.List)
                        .ToDictionary(c => c.Base.Id);
                    var mergedList = requestedIds
                        .Where(mergedById.ContainsKey)
                        .Select(id => mergedById[id])
                        .ToList();

                    var mergedAvatarWiki = new Dictionary<string, string>(cachedAvatarWiki);
                    foreach (var kvp in data.AvatarWiki)
                        mergedAvatarWiki[kvp.Key] = kvp.Value;

                    var mergedWeaponWiki = new Dictionary<string, string>(cachedWeaponWiki);
                    foreach (var kvp in data.WeaponWiki)
                        mergedWeaponWiki[kvp.Key] = kvp.Value;

                    return Result<GenshinCharacterDetail>.Success(new GenshinCharacterDetail
                    {
                        List = mergedList,
                        AvatarWiki = mergedAvatarWiki,
                        WeaponWiki = mergedWeaponWiki
                    }, requestUri: requestUri);
                });
        }
        catch (OperationCanceledException)
        {
            return Result<GenshinCharacterDetail>.FromCancellation(cancellationToken, requestUri);
        }
        catch (Exception e)
        {
            GameApiLog.LogException(m_Logger, $"{HoYoLabDomains.PublicApi}{BasePath}/character/detail", context.UserId, e);
            return Result<GenshinCharacterDetail>.Failure(StatusCode.BotError,
                "An error occurred while retrieving character data", requestUri);
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
