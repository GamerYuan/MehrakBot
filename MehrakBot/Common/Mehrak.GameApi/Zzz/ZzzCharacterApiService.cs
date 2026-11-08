#region

using Mehrak.Domain.Cache;
using Mehrak.Domain.Character;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.GameApi.Shared;
using Mehrak.GameApi.Shared.Types;
using Mehrak.GameApi.Zzz.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.Zzz;

internal class ZzzCharacterApiService : ICharacterApiService<ZzzBasicAvatarData, ZzzFullAvatarData, CharacterApiContext>
{
    private const string BasePath = "/event/game_record_zzz/api/zzz/avatar";

    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<ZzzCharacterApiService> m_Logger;
    private readonly ICacheService m_Cache;
    private const int CacheExpirationMinutes = 10;
    private const int CharacterDetailCacheMinutes = 2;
    private const int WikiCacheMinutes = 30;

    public ZzzCharacterApiService(ICacheService cache,
        IHttpClientFactory httpClientFactory,
        ILogger<ZzzCharacterApiService> logger)
    {
        m_Cache = cache;
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<IEnumerable<ZzzBasicAvatarData>>> GetAllCharactersAsync(CharacterApiContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(context.Region) || string.IsNullOrEmpty(context.GameUid))
        {
            m_Logger.LogError(LogMessages.InvalidRegionOrUid);
            return Result<IEnumerable<ZzzBasicAvatarData>>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        var requestUri =
            $"{HoYoLabDomains.PublicApi}{BasePath}/basic?server={context.Region}&role_id={context.GameUid}";

        try
        {
            using var scope = new HoYoLabRequestScope(cancellationToken);

            var cacheKey = $"zzz_characters_{context.GameUid}";
            var cachedEntry = await m_Cache.GetAsync<IEnumerable<ZzzBasicAvatarData>>(cacheKey, scope.Token);

            if (cachedEntry != null)
            {
                return Result<IEnumerable<ZzzBasicAvatarData>>.Success(cachedEntry)!;
            }

            return await HoYoLabApi.ExecuteAsync(
                new HoYoLabRequest
                {
                    RequestUri = requestUri,
                    Headers = [new KeyValuePair<string, string>("X-Rpc-Language", "en-us")]
                },
                new HoYoLabEnvelopePolicy<ZzzBasicAvatarResponse>
                {
                    HasData = data => data.AvatarList is { Count: > 0 },
                    UnknownRetcode = "An unknown error occurred when accessing HoYoLAB API. Please try again later",
                    EmptyData = "Failed to retrieve character list data"
                },
                new HoYoLabCredentials(context.UserId, context.LtUid, context.LToken),
                m_HttpClientFactory, m_Logger, scope.Token,
                async data =>
                {
                    var cacheEntry = new CharacterListCacheEntry<ZzzBasicAvatarData>(cacheKey,
                        data.AvatarList, TimeSpan.FromMinutes(CacheExpirationMinutes));

                    await m_Cache.SetAsync(cacheEntry, scope.Token);

                    return Result<IEnumerable<ZzzBasicAvatarData>>.Success(data.AvatarList, requestUri: requestUri);
                });
        }
        catch (OperationCanceledException)
        {
            return Result<IEnumerable<ZzzBasicAvatarData>>.FromCancellation(cancellationToken, requestUri);
        }
        catch (Exception e)
        {
            GameApiLog.LogException(m_Logger, $"{HoYoLabDomains.PublicApi}{BasePath}/basic", context.UserId, e);
            return Result<IEnumerable<ZzzBasicAvatarData>>.Failure(StatusCode.BotError,
                "An error occurred while retrieving character data", requestUri);
        }
    }

    public async Task<Result<ZzzFullAvatarData>> GetCharacterDetailAsync(CharacterApiContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(context.Region) || string.IsNullOrEmpty(context.GameUid) || context.CharacterId == 0)
        {
            m_Logger.LogError(LogMessages.InvalidRegionOrUid);
            return Result<ZzzFullAvatarData>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        string? requestUri = null;

        try
        {
            using var scope = new HoYoLabRequestScope(cancellationToken);

            // Try to load avatar data from cache
            var avatarCacheKey = $"zzz_char:{context.GameUid}:{context.CharacterId}";
            var cachedAvatar = await m_Cache.GetAsync<ZzzAvatarData>(avatarCacheKey, scope.Token);

            if (cachedAvatar != null)
            {
                // Avatar hit — load wiki entries in parallel using IDs from the cached avatar
                var avatarWikiTask = m_Cache.GetAsync<string>(
                    $"zzz_avatar_wiki:{context.CharacterId}", scope.Token);

                var strategyWikiTask = m_Cache.GetAsync<string>(
                    $"zzz_strategy_wiki:{context.CharacterId}", scope.Token);

                var equipWikiTasks = cachedAvatar.Equip
                    .Select(e => m_Cache.GetAsync<string>($"zzz_equip_wiki:{e.Id}", scope.Token))
                    .ToList();

                var weaponWikiTask = cachedAvatar.Weapon != null
                    ? m_Cache.GetAsync<string>($"zzz_weapon_wiki:{cachedAvatar.Weapon.Id}", scope.Token)
                    : Task.FromResult<string?>(null);

                await Task.WhenAll([avatarWikiTask, strategyWikiTask, weaponWikiTask, .. equipWikiTasks]);

                // Assemble wiki dicts from individual cached entries
                var avatarWikiVal = await avatarWikiTask;
                if (avatarWikiVal != null) // ponytail: wiki cache miss means stale cache, re-fetch from API
                {
                    var avatarWiki = new Dictionary<string, string> { [context.CharacterId.ToString()] = avatarWikiVal };

                    var strategyWiki = new Dictionary<string, string>();
                    var strategyWikiVal = await strategyWikiTask;
                    if (strategyWikiVal != null)
                        strategyWiki[context.CharacterId.ToString()] = strategyWikiVal;

                    var equipWiki = new Dictionary<string, string>();
                    for (var i = 0; i < cachedAvatar.Equip.Count; i++)
                    {
                        var val = await equipWikiTasks[i];
                        if (val != null)
                            equipWiki[cachedAvatar.Equip[i].Id.ToString()] = val;
                    }

                    var weaponWiki = new Dictionary<string, string>();
                    var weaponVal = await weaponWikiTask;
                    if (weaponVal != null && cachedAvatar.Weapon != null)
                        weaponWiki[cachedAvatar.Weapon.Id.ToString()] = weaponVal;

                    return Result<ZzzFullAvatarData>.Success(new ZzzFullAvatarData
                    {
                        AvatarList = [cachedAvatar],
                        AvatarWiki = avatarWiki,
                        EquipWiki = equipWiki,
                        WeaponWiki = weaponWiki,
                        StrategyWiki = strategyWiki
                    });
                }
            }

            // Cache miss — fetch from API
            requestUri =
                $"{HoYoLabDomains.PublicApi}{BasePath}/info?id_list[]={context.CharacterId}&server={context.Region}&role_id={context.GameUid}&need_wiki=true";

            return await HoYoLabApi.ExecuteAsync(
                new HoYoLabRequest
                {
                    RequestUri = requestUri,
                    Headers = [new KeyValuePair<string, string>("X-Rpc-Language", "en-us")]
                },
                new HoYoLabEnvelopePolicy<ZzzFullAvatarData>
                {
                    HasData = data => data.AvatarList is { Count: > 0 },
                    UnknownRetcode = "An unknown error occurred when accessing HoYoLAB API. Please try again later",
                    EmptyData = "Failed to retrieve character data"
                },
                new HoYoLabCredentials(context.UserId, context.LtUid, context.LToken),
                m_HttpClientFactory, m_Logger, scope.Token,
                async data =>
                {
                    // Cache each entry individually
                    var cacheTasks = new List<Task>();

                    foreach (var avatar in data.AvatarList)
                    {
                        cacheTasks.Add(m_Cache.SetAsync(
                            new CacheEntryBase<ZzzAvatarData>(
                                $"zzz_char:{context.GameUid}:{avatar.Id}",
                                avatar,
                                TimeSpan.FromMinutes(CharacterDetailCacheMinutes)),
                            scope.Token));
                    }

                    if (data.AvatarWiki != null)
                    {
                        foreach (var (id, val) in data.AvatarWiki)
                        {
                            cacheTasks.Add(m_Cache.SetAsync(
                                new CacheEntryBase<string>($"zzz_avatar_wiki:{id}", val,
                                    TimeSpan.FromMinutes(WikiCacheMinutes)),
                                scope.Token));
                        }
                    }

                    if (data.EquipWiki != null)
                    {
                        foreach (var (id, val) in data.EquipWiki)
                        {
                            cacheTasks.Add(m_Cache.SetAsync(
                                new CacheEntryBase<string>($"zzz_equip_wiki:{id}", val,
                                    TimeSpan.FromMinutes(WikiCacheMinutes)),
                                scope.Token));
                        }
                    }

                    if (data.WeaponWiki != null)
                    {
                        foreach (var (id, val) in data.WeaponWiki)
                        {
                            cacheTasks.Add(m_Cache.SetAsync(
                                new CacheEntryBase<string>($"zzz_weapon_wiki:{id}", val,
                                    TimeSpan.FromMinutes(WikiCacheMinutes)),
                                scope.Token));
                        }
                    }

                    if (data.StrategyWiki != null)
                    {
                        foreach (var (id, val) in data.StrategyWiki)
                        {
                            cacheTasks.Add(m_Cache.SetAsync(
                                new CacheEntryBase<string>($"zzz_strategy_wiki:{id}", val,
                                    TimeSpan.FromMinutes(WikiCacheMinutes)),
                                scope.Token));
                        }
                    }

                    await Task.WhenAll(cacheTasks);

                    return Result<ZzzFullAvatarData>.Success(data, requestUri: requestUri);
                });
        }
        catch (OperationCanceledException)
        {
            return Result<ZzzFullAvatarData>.FromCancellation(cancellationToken, requestUri);
        }
        catch (Exception e)
        {
            GameApiLog.LogException(m_Logger, $"{HoYoLabDomains.PublicApi}{BasePath}/info", context.UserId, e);
            return Result<ZzzFullAvatarData>.Failure(StatusCode.BotError,
                "An error occurred while retrieving character data", requestUri);
        }
    }
}
