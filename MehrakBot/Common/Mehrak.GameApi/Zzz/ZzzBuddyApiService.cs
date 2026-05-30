using System.Text.Json;
using Mehrak.Domain.Cache;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Shared;
using Mehrak.GameApi.Shared.Types;
using Mehrak.GameApi.Zzz.Types;
using Microsoft.Extensions.Logging;

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

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(IApiService.MaxTimeoutSeconds));

            var cacheKey = $"zzz_buddies_{context.GameUid}";
            var cachedEntry = await m_Cache.GetAsync<IEnumerable<ZzzBuddyData>>(cacheKey, timeoutCts.Token);

            if (cachedEntry != null)
            {
                return Result<IEnumerable<ZzzBuddyData>>.Success(cachedEntry)!;
            }

            var requestUri =
                $"{HoYoLabDomains.PublicApi}{BasePath}?server={context.Region}&role_id={context.GameUid}";

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
                return Result<IEnumerable<ZzzBuddyData>>.Failure(StatusCode.ExternalServerError,
                    "Failed to retrieve character list data", requestUri);
            }

            var json =
                await JsonSerializer.DeserializeAsync<ApiResponse<ZzzBuddyResponse>>(
                    await response.Content.ReadAsStreamAsync(timeoutCts.Token), (JsonSerializerOptions?)null, timeoutCts.Token);

            if (json?.Data == null || json.Data.List.Count == 0)
            {
                m_Logger.LogError(LogMessages.EmptyResponseData, requestUri, context.UserId);
                return Result<IEnumerable<ZzzBuddyData>>.Failure(StatusCode.ExternalServerError,
                    "Failed to retrieve character list data", requestUri);
            }

            // Info-level API retcode after parse
            m_Logger.LogInformation(LogMessages.InboundHttpResponseWithRetcode, (int)response.StatusCode, requestUri, json.Retcode, context.UserId);

            if (json.Retcode == 10001)
            {
                m_Logger.LogError(LogMessages.InvalidCredentials, context.UserId);
                return Result<IEnumerable<ZzzBuddyData>>.Failure(StatusCode.Unauthorized,
                    "Invalid HoYoLAB UID or Cookies. Please authenticate again.", requestUri);
            }

            if (json.Retcode != 0)
            {
                m_Logger.LogError(LogMessages.UnknownRetcode, json.Retcode, context.UserId, requestUri, json);
                return Result<IEnumerable<ZzzBuddyData>>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later", requestUri);
            }

            var cacheEntry = new CharacterListCacheEntry<ZzzBuddyData>(cacheKey,
                json.Data.List, TimeSpan.FromMinutes(CacheExpirationMinutes));

            await m_Cache.SetAsync(cacheEntry, cancellationToken);

            return Result<IEnumerable<ZzzBuddyData>>.Success(json.Data.List, requestUri: requestUri);
        }
        catch (OperationCanceledException)
        {
            return Result<IEnumerable<ZzzBuddyData>>.FromCancellation(cancellationToken);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, LogMessages.ExceptionOccurred,
                $"{HoYoLabDomains.PublicApi}{BasePath}", context.UserId);
            return Result<IEnumerable<ZzzBuddyData>>.Failure(StatusCode.BotError,
                "An error occurred while retrieving character data");
        }
    }
}
