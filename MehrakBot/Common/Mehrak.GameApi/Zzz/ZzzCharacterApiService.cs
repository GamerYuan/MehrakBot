#region

using System.Text.Json;
using System.Text.Json.Serialization;
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

    public async Task<Result<IEnumerable<ZzzBasicAvatarData>>> GetAllCharactersAsync(CharacterApiContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(context.Region) || string.IsNullOrEmpty(context.GameUid))
        {
            m_Logger.LogError(LogMessages.InvalidRegionOrUid);
            return Result<IEnumerable<ZzzBasicAvatarData>>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(IApiService.MaxTimeoutSeconds));

            var cacheKey = $"zzz_characters_{context.GameUid}";
            var cachedEntry = await m_Cache.GetAsync<IEnumerable<ZzzBasicAvatarData>>(cacheKey, timeoutCts.Token);

            if (cachedEntry != null)
            {
                return Result<IEnumerable<ZzzBasicAvatarData>>.Success(cachedEntry)!;
            }

            var requestUri =
                $"{HoYoLabDomains.PublicApi}{BasePath}/basic?server={context.Region}&role_id={context.GameUid}";

            m_Logger.LogInformation(LogMessages.PreparingRequest, requestUri);

            var httpClient = m_HttpClientFactory.CreateClient("Default");

            HttpRequestMessage request = new()
            {
                RequestUri = new Uri(requestUri)
            };
            request.Headers.Add("Cookie", $"ltuid_v2={context.LtUid}; ltoken_v2={context.LToken}");
            request.Headers.Add("X-Rpc-Language", "en-us");

            var response = await httpClient.SendAsync(request, timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError(LogMessages.NonSuccessStatusCode, response.StatusCode, requestUri);
                return Result<IEnumerable<ZzzBasicAvatarData>>.Failure(StatusCode.ExternalServerError,
                    "Failed to retrieve character list data", requestUri);
            }

            var json =
                await JsonSerializer.DeserializeAsync<ApiResponse<ZzzBasicAvatarResponse>>(
                    await response.Content.ReadAsStreamAsync(timeoutCts.Token), JsonOptions, timeoutCts.Token);

            if (json?.Data == null || json.Data.AvatarList.Count == 0)
            {
                m_Logger.LogError(LogMessages.EmptyResponseData, requestUri, context.UserId);
                return Result<IEnumerable<ZzzBasicAvatarData>>.Failure(StatusCode.ExternalServerError,
                    "Failed to retrieve character list data", requestUri);
            }

            // Info-level API retcode after parse
            m_Logger.LogInformation(LogMessages.InboundHttpResponseWithRetcode, (int)response.StatusCode, requestUri, json.Retcode, context.UserId);

            if (json.Retcode == 10001)
            {
                m_Logger.LogError(LogMessages.InvalidCredentials, context.UserId);
                return Result<IEnumerable<ZzzBasicAvatarData>>.Failure(StatusCode.Unauthorized,
                    "Invalid HoYoLAB UID or Cookies. Please authenticate again.", requestUri);
            }

            if (json.Retcode != 0)
            {
                m_Logger.LogError(LogMessages.UnknownRetcode, json.Retcode, context.UserId, requestUri, json);
                return Result<IEnumerable<ZzzBasicAvatarData>>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later", requestUri);
            }

            var cacheEntry = new CharacterListCacheEntry<ZzzBasicAvatarData>(cacheKey,
                json.Data.AvatarList, TimeSpan.FromMinutes(CacheExpirationMinutes));

            await m_Cache.SetAsync(cacheEntry, cancellationToken);

            return Result<IEnumerable<ZzzBasicAvatarData>>.Success(json.Data.AvatarList, requestUri: requestUri);
        }
        catch (OperationCanceledException)
        {
            return Result<IEnumerable<ZzzBasicAvatarData>>.FromCancellation(cancellationToken);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, LogMessages.ExceptionOccurred,
                $"{HoYoLabDomains.PublicApi}{BasePath}/basic", context.UserId);
            return Result<IEnumerable<ZzzBasicAvatarData>>.Failure(StatusCode.BotError,
                "An error occurred while retrieving character data");
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

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(IApiService.MaxTimeoutSeconds));

            // Try to load avatar data from cache
            var avatarCacheKey = $"zzz_char:{context.GameUid}:{context.CharacterId}";
            var cachedAvatar = await m_Cache.GetAsync<ZzzAvatarData>(avatarCacheKey, timeoutCts.Token);

            if (cachedAvatar != null)
            {
                // Avatar hit — load wiki entries in parallel using IDs from the cached avatar
                var avatarWikiTask = m_Cache.GetAsync<string>(
                    $"zzz_avatar_wiki:{context.CharacterId}", timeoutCts.Token);

                var strategyWikiTask = m_Cache.GetAsync<string>(
                    $"zzz_strategy_wiki:{context.CharacterId}", timeoutCts.Token);

                var equipWikiTasks = cachedAvatar.Equip
                    .Select(e => m_Cache.GetAsync<string>($"zzz_equip_wiki:{e.Id}", timeoutCts.Token))
                    .ToList();

                var weaponWikiTask = cachedAvatar.Weapon != null
                    ? m_Cache.GetAsync<string>($"zzz_weapon_wiki:{cachedAvatar.Weapon.Id}", timeoutCts.Token)
                    : Task.FromResult<string?>(null);

                await Task.WhenAll([avatarWikiTask, strategyWikiTask, weaponWikiTask, .. equipWikiTasks]);

                // Assemble wiki dicts from individual cached entries
                var avatarWiki = new Dictionary<string, string>();
                var avatarWikiVal = await avatarWikiTask;
                if (avatarWikiVal != null)
                    avatarWiki[context.CharacterId.ToString()] = avatarWikiVal;

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

            // Cache miss — fetch from API
            var requestUri =
                $"{HoYoLabDomains.PublicApi}{BasePath}/info?id_list[]={context.CharacterId}&server={context.Region}&role_id={context.GameUid}&need_wiki=true";

            m_Logger.LogInformation(LogMessages.PreparingRequest, requestUri);

            var httpClient = m_HttpClientFactory.CreateClient("Default");

            HttpRequestMessage request = new()
            {
                RequestUri = new Uri(requestUri)
            };
            request.Headers.Add("Cookie", $"ltuid_v2={context.LtUid}; ltoken_v2={context.LToken}");
            request.Headers.Add("X-Rpc-Language", "en-us");

            var response = await httpClient.SendAsync(request, timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError(LogMessages.NonSuccessStatusCode, response.StatusCode, requestUri);
                return Result<ZzzFullAvatarData>.Failure(StatusCode.ExternalServerError,
                    "Failed to retrieve character data");
            }

            var json =
                await JsonSerializer.DeserializeAsync<ApiResponse<ZzzFullAvatarData>>(
                    await response.Content.ReadAsStreamAsync(timeoutCts.Token), JsonOptions, timeoutCts.Token);

            if (json?.Data == null || json.Data.AvatarList.Count == 0)
            {
                m_Logger.LogError(LogMessages.EmptyResponseData, requestUri, context.UserId);
                return Result<ZzzFullAvatarData>.Failure(StatusCode.ExternalServerError,
                    "Failed to retrieve character data");
            }

            // Info-level API retcode after parse
            m_Logger.LogInformation(LogMessages.InboundHttpResponseWithRetcode, (int)response.StatusCode, requestUri, json.Retcode, context.UserId);

            if (json.Retcode == 10001)
            {
                m_Logger.LogError(LogMessages.InvalidCredentials, context.UserId);
                return Result<ZzzFullAvatarData>.Failure(StatusCode.Unauthorized,
                    "Invalid HoYoLAB UID or Cookies. Please authenticate again.");
            }

            if (json.Retcode != 0)
            {
                m_Logger.LogError(LogMessages.UnknownRetcode, json.Retcode, context.UserId, requestUri, json);
                return Result<ZzzFullAvatarData>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later");
            }

            var data = json.Data;

            // Cache each entry individually
            var cacheTasks = new List<Task>();

            foreach (var avatar in data.AvatarList)
            {
                cacheTasks.Add(m_Cache.SetAsync(
                    new CacheEntryBase<ZzzAvatarData>(
                        $"zzz_char:{context.GameUid}:{avatar.Id}",
                        avatar,
                        TimeSpan.FromMinutes(CharacterDetailCacheMinutes)),
                    cancellationToken));
            }

            if (data.AvatarWiki != null)
            {
                foreach (var (id, val) in data.AvatarWiki)
                {
                    cacheTasks.Add(m_Cache.SetAsync(
                        new CacheEntryBase<string>($"zzz_avatar_wiki:{id}", val,
                            TimeSpan.FromMinutes(WikiCacheMinutes)),
                        cancellationToken));
                }
            }

            if (data.EquipWiki != null)
            {
                foreach (var (id, val) in data.EquipWiki)
                {
                    cacheTasks.Add(m_Cache.SetAsync(
                        new CacheEntryBase<string>($"zzz_equip_wiki:{id}", val,
                            TimeSpan.FromMinutes(WikiCacheMinutes)),
                        cancellationToken));
                }
            }

            if (data.WeaponWiki != null)
            {
                foreach (var (id, val) in data.WeaponWiki)
                {
                    cacheTasks.Add(m_Cache.SetAsync(
                        new CacheEntryBase<string>($"zzz_weapon_wiki:{id}", val,
                            TimeSpan.FromMinutes(WikiCacheMinutes)),
                        cancellationToken));
                }
            }

            if (data.StrategyWiki != null)
            {
                foreach (var (id, val) in data.StrategyWiki)
                {
                    cacheTasks.Add(m_Cache.SetAsync(
                        new CacheEntryBase<string>($"zzz_strategy_wiki:{id}", val,
                            TimeSpan.FromMinutes(WikiCacheMinutes)),
                        cancellationToken));
                }
            }

            await Task.WhenAll(cacheTasks);

            return Result<ZzzFullAvatarData>.Success(data);
        }
        catch (OperationCanceledException)
        {
            return Result<ZzzFullAvatarData>.FromCancellation(cancellationToken);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, LogMessages.ExceptionOccurred,
                $"{HoYoLabDomains.PublicApi}{BasePath}/info", context.UserId);
            return Result<ZzzFullAvatarData>.Failure(StatusCode.BotError,
                "An error occurred while retrieving character data");
        }
    }
}
