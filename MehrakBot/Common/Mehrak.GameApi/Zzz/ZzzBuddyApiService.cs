using System.Text.Json;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common;
using Mehrak.GameApi.Common.Types;
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

    public async Task<Result<IEnumerable<ZzzBuddyData>>> GetAsync(BaseHoYoApiContext context)
    {
        if (string.IsNullOrEmpty(context.Region) || string.IsNullOrEmpty(context.GameUid))
        {
            m_Logger.LogError(LogMessages.InvalidRegionOrUid);
            return Result<IEnumerable<ZzzBuddyData>>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        try
        {
            var cacheKey = $"zzz_buddies_{context.GameUid}";
            var cachedEntry = await m_Cache.GetAsync<IEnumerable<ZzzBuddyData>>(cacheKey);

            if (cachedEntry != null)
            {
                m_Logger.LogInformation(LogMessages.SuccessfullyRetrievedFromCache, context.UserId);
                return Result<IEnumerable<ZzzBuddyData>>.Success(cachedEntry)!;
            }

            m_Logger.LogDebug(LogMessages.CacheMiss, cacheKey, context.UserId);

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

            // Info-level outbound request (no headers)
            m_Logger.LogInformation(LogMessages.OutboundHttpRequest, request.Method, requestUri);
            var response = await httpClient.SendAsync(request);

            // Info-level inbound response (status only)
            m_Logger.LogInformation(LogMessages.InboundHttpResponse, (int)response.StatusCode, requestUri);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError(LogMessages.NonSuccessStatusCode, response.StatusCode, requestUri);
                return Result<IEnumerable<ZzzBuddyData>>.Failure(StatusCode.ExternalServerError,
                    "Failed to retrieve character list data", requestUri);
            }

            var json =
                await JsonSerializer.DeserializeAsync<ApiResponse<ZzzBuddyResponse>>(
                    await response.Content.ReadAsStreamAsync());

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

            m_Logger.LogInformation(LogMessages.SuccessfullyRetrievedData, requestUri, context.UserId);

            var cacheEntry = new CharacterListCacheEntry<ZzzBuddyData>(cacheKey,
                json.Data.List, TimeSpan.FromMinutes(CacheExpirationMinutes));

            await m_Cache.SetAsync(cacheEntry);
            m_Logger.LogInformation(LogMessages.SuccessfullyCachedData, context.UserId, CacheExpirationMinutes);

            return Result<IEnumerable<ZzzBuddyData>>.Success(json.Data.List, requestUri: requestUri);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, LogMessages.ExceptionOccurred,
                $"{HoYoLabDomains.PublicApi}{BasePath}/basic", context.UserId);
            return Result<IEnumerable<ZzzBuddyData>>.Failure(StatusCode.BotError,
                "An error occurred while retrieving character data");
        }
    }
}
