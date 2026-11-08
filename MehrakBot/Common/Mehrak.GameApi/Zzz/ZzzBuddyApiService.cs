#region

using Mehrak.Domain.Cache;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.GameApi.Shared;
using Mehrak.GameApi.Shared.Types;
using Mehrak.GameApi.Zzz.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.Zzz;

internal class ZzzBuddyApiService : IApiService<IEnumerable<ZzzBuddyData>, BaseHoYoApiContext>
{
    private const string BasePath = "/event/game_record_zzz/api/zzz/buddy/info";

    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<ZzzBuddyApiService> m_Logger;
    private readonly ICacheService m_Cache;
    private const int CacheExpirationMinutes = 10;

    public ZzzBuddyApiService(ICacheService cache,
        IHttpClientFactory httpClientFactory,
        ILogger<ZzzBuddyApiService> logger)
    {
        m_Cache = cache;
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<IEnumerable<ZzzBuddyData>>> GetAsync(BaseHoYoApiContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(context.Region) || string.IsNullOrEmpty(context.GameUid))
        {
            m_Logger.LogError(LogMessages.InvalidRegionOrUid);
            return Result<IEnumerable<ZzzBuddyData>>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        var requestUri =
            $"{HoYoLabDomains.PublicApi}{BasePath}?server={context.Region}&role_id={context.GameUid}";

        try
        {
            using var scope = new HoYoLabRequestScope(cancellationToken);

            var cacheKey = $"zzz_buddies_{context.GameUid}";
            var cachedEntry = await m_Cache.GetAsync<IEnumerable<ZzzBuddyData>>(cacheKey, scope.Token);

            if (cachedEntry != null)
            {
                return Result<IEnumerable<ZzzBuddyData>>.Success(cachedEntry)!;
            }

            return await HoYoLabApi.ExecuteAsync(
                new HoYoLabRequest
                {
                    RequestUri = requestUri,
                    Headers = [new KeyValuePair<string, string>("X-Rpc-Language", "en-us")]
                },
                new HoYoLabEnvelopePolicy<ZzzBuddyResponse>
                {
                    HasData = data => data.List is { Count: > 0 },
                    UnknownRetcode = "An unknown error occurred when accessing HoYoLAB API. Please try again later",
                    EmptyData = "Failed to retrieve character list data"
                },
                new HoYoLabCredentials(context.UserId, context.LtUid, context.LToken),
                m_HttpClientFactory, m_Logger, scope.Token,
                async data =>
                {
                    var cacheEntry = new CharacterListCacheEntry<ZzzBuddyData>(cacheKey,
                        data.List, TimeSpan.FromMinutes(CacheExpirationMinutes));

                    await m_Cache.SetAsync(cacheEntry, scope.Token);

                    return Result<IEnumerable<ZzzBuddyData>>.Success(data.List, requestUri: requestUri);
                });
        }
        catch (OperationCanceledException)
        {
            return Result<IEnumerable<ZzzBuddyData>>.FromCancellation(cancellationToken, requestUri);
        }
        catch (Exception e)
        {
            GameApiLog.LogException(m_Logger, $"{HoYoLabDomains.PublicApi}{BasePath}", context.UserId, e);
            return Result<IEnumerable<ZzzBuddyData>>.Failure(StatusCode.BotError,
                "An error occurred while retrieving character data", requestUri);
        }
    }
}
