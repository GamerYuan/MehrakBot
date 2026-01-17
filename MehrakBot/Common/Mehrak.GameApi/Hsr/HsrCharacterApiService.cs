#region

using System.Text.Json;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Hsr.Types;
using Mehrak.GameApi.Utilities;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.Hsr;

public class
    HsrCharacterApiService : ICharacterApiService<HsrBasicCharacterData, HsrCharacterInformation, CharacterApiContext>
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
            m_Logger.LogError(LogMessages.InvalidRegionOrUid);
            return Result<IEnumerable<HsrBasicCharacterData>>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        try
        {
            var cacheKey = $"hsr_characters_{context.GameUid}";
            var cachedData = await m_Cache.GetAsync<IEnumerable<HsrBasicCharacterData>>(cacheKey);

            // Try to get data from cache first
            if (cachedData != null)
            {
                m_Logger.LogInformation(LogMessages.SuccessfullyRetrievedFromCache, context.UserId);
                return Result<IEnumerable<HsrBasicCharacterData>>.Success(cachedData);
            }

            m_Logger.LogDebug(LogMessages.CacheMiss, cacheKey, context.UserId);

            var requestUri =
                $"{HoYoLabDomains.PublicApi}{ApiEndpoint}?server={context.Region}&role_id={context.GameUid}&need_wiki=true";

            m_Logger.LogInformation(LogMessages.PreparingRequest, requestUri);

            var client = m_HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new()
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

            // Info-level outbound request (no headers)
            m_Logger.LogInformation(LogMessages.OutboundHttpRequest, request.Method, requestUri);
            var response = await client.SendAsync(request);

            // Info-level inbound response (status only)
            m_Logger.LogInformation(LogMessages.InboundHttpResponse, (int)response.StatusCode, requestUri);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError(LogMessages.NonSuccessStatusCode, response.StatusCode, requestUri);
                return Result<IEnumerable<HsrBasicCharacterData>>.Failure(StatusCode.ExternalServerError,
                    "Failed to retrieve character information", requestUri);
            }

            var json =
                await JsonSerializer.DeserializeAsync<ApiResponse<HsrBasicCharacterData>>(
                    await response.Content.ReadAsStreamAsync());

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

            m_Logger.LogInformation(LogMessages.SuccessfullyRetrievedData, requestUri, context.UserId);

            HsrBasicCharacterData[] result = [json.Data];

            await m_Cache.SetAsync(
                new CharacterListCacheEntry<HsrBasicCharacterData>(cacheKey, result,
                    TimeSpan.FromMinutes(CacheExpirationMinutes)));
            m_Logger.LogInformation(LogMessages.SuccessfullyCachedData, context.UserId, CacheExpirationMinutes);

            return Result<IEnumerable<HsrBasicCharacterData>>.Success(result, requestUri: requestUri);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, LogMessages.ExceptionOccurred,
                $"{HoYoLabDomains.PublicApi}{ApiEndpoint}", context.UserId);
            return Result<IEnumerable<HsrBasicCharacterData>>.Failure(StatusCode.BotError,
                "An error occurred while retrieving character information");
        }
    }

    /// <summary>
    /// Stub! DO NOT USE!
    /// </summary>
    public Task<Result<HsrCharacterInformation>> GetCharacterDetailAsync(CharacterApiContext context)
    {
        throw new NotImplementedException();
    }
}
