#region

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

        var requestUri =
            $"{HoYoLabDomains.PublicApi}{ApiEndpoint}?server={context.Region}&role_id={context.GameUid}&need_wiki=true";

        try
        {
            using var scope = new HoYoLabRequestScope(cancellationToken);

            var cacheKey = $"hsr_characters_{context.GameUid}";
            var cachedData = await m_Cache.GetAsync<IEnumerable<HsrBasicCharacterData>>(cacheKey, scope.Token);

            // Try to get data from cache first
            if (cachedData != null)
            {
                return Result<IEnumerable<HsrBasicCharacterData>>.Success(cachedData);
            }

            return await HoYoLabApi.ExecuteAsync(
                CreateRequest(requestUri),
                new HoYoLabEnvelopePolicy<HsrBasicCharacterData>
                {
                    HasData = data => data.AvatarList is { Count: > 0 },
                    UnknownRetcode = "An unknown error occurred when accessing HoYoLAB API. Please try again later",
                    EmptyData = "Failed to retrieve character information"
                },
                new HoYoLabCredentials(context.UserId, context.LtUid, context.LToken),
                m_HttpClientFactory, m_Logger, scope.Token,
                async data =>
                {
                    HsrBasicCharacterData[] result = [data];

                    await m_Cache.SetAsync(
                        new CharacterListCacheEntry<HsrBasicCharacterData>(cacheKey, result,
                            TimeSpan.FromMinutes(CacheExpirationMinutes)), scope.Token);

                    return Result<IEnumerable<HsrBasicCharacterData>>.Success(result, requestUri: requestUri);
                });
        }
        catch (OperationCanceledException)
        {
            return Result<IEnumerable<HsrBasicCharacterData>>.FromCancellation(cancellationToken, requestUri);
        }
        catch (Exception e)
        {
            GameApiLog.LogException(m_Logger, $"{HoYoLabDomains.PublicApi}{ApiEndpoint}", context.UserId, e);
            return Result<IEnumerable<HsrBasicCharacterData>>.Failure(StatusCode.BotError,
                "An error occurred while retrieving character information", requestUri);
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

        string? requestUri = null;

        try
        {
            using var scope = new HoYoLabRequestScope(cancellationToken);

            // Check cache for the requested character
            var charCacheKey = $"hsr_char:{context.GameUid}:{context.CharacterId}";
            var cachedChar = await m_Cache.GetAsync<HsrCharacterInformation>(charCacheKey, scope.Token);

            if (cachedChar != null)
            {
                // Character cached — load wiki entries from cache if available
                var equipWiki = new Dictionary<string, string>();
                if (cachedChar.Equip != null)
                {
                    var equipKey = $"hsr_equip_wiki:{cachedChar.Equip.Id}";
                    var cachedEquip = await m_Cache.GetAsync<string>(equipKey, scope.Token);
                    if (cachedEquip != null)
                        equipWiki[cachedChar.Equip.Id.ToString()] = cachedEquip;
                }

                var relicWiki = new Dictionary<string, string>();
                var relicIds = cachedChar.Relics.Select(r => r.Id)
                    .Concat(cachedChar.Ornaments.Select(r => r.Id))
                    .Distinct().ToArray();

                var relicTasks = relicIds.Select(id =>
                    m_Cache.GetAsync<string>($"hsr_relic_wiki:{id}", scope.Token));
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
            requestUri =
                $"{HoYoLabDomains.PublicApi}{ApiEndpoint}?server={context.Region}&role_id={context.GameUid}&need_wiki=true";

            return await HoYoLabApi.ExecuteAsync(
                CreateRequest(requestUri),
                new HoYoLabEnvelopePolicy<HsrBasicCharacterData>
                {
                    HasData = data => data.AvatarList is { Count: > 0 },
                    UnknownRetcode = "An unknown error occurred when accessing HoYoLAB API. Please try again later",
                    EmptyData = "Failed to retrieve character information"
                },
                new HoYoLabCredentials(context.UserId, context.LtUid, context.LToken),
                m_HttpClientFactory, m_Logger, scope.Token,
                async data =>
                {
                    // Cache each character individually
                    var charCacheTasks = data.AvatarList.Select(avatar =>
                        m_Cache.SetAsync(
                            new CacheEntryBase<HsrCharacterInformation>(
                                $"hsr_char:{context.GameUid}:{avatar.Id}",
                                avatar,
                                TimeSpan.FromMinutes(CharacterDetailCacheMinutes)),
                            scope.Token));
                    await Task.WhenAll(charCacheTasks);

                    // Cache equip wiki entries
                    var equipWikiCacheTasks = data.EquipWiki.Select(kvp =>
                        m_Cache.SetAsync(
                            new CacheEntryBase<string>(
                                $"hsr_equip_wiki:{kvp.Key}",
                                kvp.Value,
                                TimeSpan.FromMinutes(WikiCacheMinutes)),
                            scope.Token));
                    await Task.WhenAll(equipWikiCacheTasks);

                    // Cache relic wiki entries
                    var relicWikiCacheTasks = data.RelicWiki.Select(kvp =>
                        m_Cache.SetAsync(
                            new CacheEntryBase<string>(
                                $"hsr_relic_wiki:{kvp.Key}",
                                kvp.Value,
                                TimeSpan.FromMinutes(WikiCacheMinutes)),
                            scope.Token));
                    await Task.WhenAll(relicWikiCacheTasks);

                    // Find and return the requested character
                    var requestedChar = data.AvatarList.FirstOrDefault(x => x.Id == context.CharacterId);
                    if (requestedChar == null)
                    {
                        m_Logger.LogError("Requested character {CharacterId} not found in API response", context.CharacterId);
                        return Result<HsrBasicCharacterData>.Failure(StatusCode.ExternalServerError,
                            "Requested character not found", requestUri);
                    }

                    return Result<HsrBasicCharacterData>.Success(new HsrBasicCharacterData
                    {
                        AvatarList = [requestedChar],
                        EquipWiki = data.EquipWiki,
                        RelicWiki = data.RelicWiki
                    }, requestUri: requestUri);
                });
        }
        catch (OperationCanceledException)
        {
            return Result<HsrBasicCharacterData>.FromCancellation(cancellationToken, requestUri);
        }
        catch (Exception e)
        {
            GameApiLog.LogException(m_Logger, $"{HoYoLabDomains.PublicApi}{ApiEndpoint}", context.UserId, e);
            return Result<HsrBasicCharacterData>.Failure(StatusCode.BotError,
                "An error occurred while retrieving character information", requestUri);
        }
    }

    private static HoYoLabRequest CreateRequest(string requestUri)
    {
        return new HoYoLabRequest
        {
            RequestUri = requestUri,
            Headers =
            [
                new KeyValuePair<string, string>("X-Rpc-Client_type", "5"),
                new KeyValuePair<string, string>("X-Rpc-App_version", "1.5.0"),
                new KeyValuePair<string, string>("X-Rpc-Language", "en-us"),
                new KeyValuePair<string, string>("DS", DSGenerator.GenerateDS())
            ],
            Failures = new HoYoLabFailureMessages
            {
                Unauthorized = "Invalid HoYoLAB UID or Cookies. Please authenticate again.",
                Parse = "Failed to retrieve character information",
                NonSuccessStatus = "Failed to retrieve character information"
            }
        };
    }
}
