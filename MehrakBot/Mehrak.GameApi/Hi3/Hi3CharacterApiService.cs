using System.Text.Json;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Hi3.Types;
using Mehrak.GameApi.Utilities;
using Microsoft.Extensions.Logging;

namespace Mehrak.GameApi.Hi3;

internal class Hi3CharacterApiService : ICharacterApiService<Hi3CharacterDetail, Hi3CharacterDetail, CharacterApiContext>
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ICacheService m_Cache;
    private readonly ILogger<Hi3CharacterApiService> m_Logger;

    private static readonly string ApiEndpoint = "/game_record/honkai3rd/api/characters";
    private const int CacheExpirationMinutes = 10;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
    };

    public Hi3CharacterApiService(
        IHttpClientFactory httpClientFactory,
        ICacheService cache,
        ILogger<Hi3CharacterApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Cache = cache;
        m_Logger = logger;
    }

    public async Task<Result<IEnumerable<Hi3CharacterDetail>>> GetAllCharactersAsync(CharacterApiContext context)
    {
        if (string.IsNullOrEmpty(context.Region) || string.IsNullOrEmpty(context.GameUid))
        {
            m_Logger.LogError(LogMessages.InvalidRegionOrUid);
            return Result<IEnumerable<Hi3CharacterDetail>>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        try
        {
            string cacheKey = $"hi3_characters_{context.Region}_{context.GameUid}";
            var cachedData = await m_Cache.GetAsync<IEnumerable<Hi3CharacterDetail>>(cacheKey);

            // Try to get data from cache first
            if (cachedData != null)
            {
                m_Logger.LogInformation(LogMessages.SuccessfullyRetrievedFromCache, context.UserId);
                return Result<IEnumerable<Hi3CharacterDetail>>.Success(cachedData);
            }

            m_Logger.LogDebug(LogMessages.CacheMiss, cacheKey, context.UserId);

            var requestUri =
                $"{HoYoLabDomains.BbsApi}{ApiEndpoint}?server={context.Region}&role_id={context.GameUid}";

            m_Logger.LogInformation(LogMessages.PreparingRequest, requestUri);

            HttpClient client = m_HttpClientFactory.CreateClient("Default");
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

            // Info-level outbound request (no headers)
            m_Logger.LogInformation(LogMessages.OutboundHttpRequest, request.Method, requestUri);
            using var response = await client.SendAsync(request);

            // Info-level inbound response (status only)
            m_Logger.LogInformation(LogMessages.InboundHttpResponse, (int)response.StatusCode, requestUri);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError(LogMessages.NonSuccessStatusCode, response.StatusCode, requestUri);
                return Result<IEnumerable<Hi3CharacterDetail>>.Failure(StatusCode.ExternalServerError,
                    "Failed to retrieve character information", requestUri);
            }

            ApiResponse<Hi3CharacterList>? json =
                await JsonSerializer.DeserializeAsync<ApiResponse<Hi3CharacterList>>(
                    await response.Content.ReadAsStreamAsync(), JsonOptions);

            if (json?.Data == null || json.Data.Characters.Count == 0)
            {
                m_Logger.LogError(LogMessages.EmptyResponseData, requestUri, context.UserId);
                return Result<IEnumerable<Hi3CharacterDetail>>.Failure(StatusCode.ExternalServerError,
                    "Failed to retrieve character information", requestUri);
            }

            // Info-level API retcode after parse
            m_Logger.LogInformation(LogMessages.InboundHttpResponseWithRetcode, (int)response.StatusCode, requestUri, json.Retcode, context.UserId);

            if (json.Retcode == 10001)
            {
                m_Logger.LogError(LogMessages.InvalidCredentials, context.UserId);
                return Result<IEnumerable<Hi3CharacterDetail>>.Failure(StatusCode.Unauthorized,
                    "Invalid HoYoLAB UID or Cookies. Please authenticate again.", requestUri);
            }

            if (json.Retcode != 0)
            {
                m_Logger.LogError(LogMessages.UnknownRetcode, json.Retcode, context.UserId, requestUri);
                return Result<IEnumerable<Hi3CharacterDetail>>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later", requestUri);
            }

            m_Logger.LogInformation(LogMessages.SuccessfullyRetrievedData, requestUri, context.UserId);

            List<Hi3CharacterDetail> result = [.. json.Data.Characters.Select(x => x.Character)];

            await m_Cache.SetAsync(
                new CharacterListCacheEntry<Hi3CharacterDetail>(cacheKey, result,
                    TimeSpan.FromMinutes(CacheExpirationMinutes)));
            m_Logger.LogInformation(LogMessages.SuccessfullyCachedData, context.UserId, CacheExpirationMinutes);

            return Result<IEnumerable<Hi3CharacterDetail>>.Success(result, requestUri: requestUri);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, LogMessages.ExceptionOccurred,
                $"{HoYoLabDomains.BbsApi}{ApiEndpoint}", context.UserId);
            return Result<IEnumerable<Hi3CharacterDetail>>.Failure(StatusCode.BotError,
                "An error occurred while retrieving character information");
        }
    }

    /// <summary>
    /// Stub! DO NOT USE!
    /// </summary>
    public Task<Result<Hi3CharacterDetail>> GetCharacterDetailAsync(CharacterApiContext context)
    {
        throw new NotSupportedException();
    }
}
