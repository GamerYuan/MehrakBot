#region

using System.Text.Json;
using System.Text.Json.Serialization;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common;
using Mehrak.GameApi.Common.Types;
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

    public async Task<Result<IEnumerable<ZzzBasicAvatarData>>> GetAllCharactersAsync(CharacterApiContext context)
    {
        if (string.IsNullOrEmpty(context.Region) || string.IsNullOrEmpty(context.GameUid))
        {
            m_Logger.LogError(LogMessages.InvalidRegionOrUid);
            return Result<IEnumerable<ZzzBasicAvatarData>>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        try
        {
            string cacheKey = $"zzz_characters_{context.GameUid}";
            var cachedEntry = await m_Cache.GetAsync<IEnumerable<ZzzBasicAvatarData>>(cacheKey);

            if (cachedEntry != null)
            {
                m_Logger.LogInformation(LogMessages.SuccessfullyRetrievedFromCache, context.UserId);
                return Result<IEnumerable<ZzzBasicAvatarData>>.Success(cachedEntry)!;
            }

            var requestUri =
                $"{HoYoLabDomains.PublicApi}{BasePath}/basic?server={context.Region}&role_id={context.GameUid}";

            m_Logger.LogInformation(LogMessages.ReceivedRequest, requestUri);

            HttpClient httpClient = m_HttpClientFactory.CreateClient("Default");

            HttpRequestMessage request = new()
            {
                RequestUri = new Uri(requestUri)
            };
            request.Headers.Add("Cookie", $"ltuid_v2={context.LtUid}; ltoken_v2={context.LToken}");
            request.Headers.Add("X-Rpc-Language", "en-us");

            // Info-level outbound request (no headers)
            m_Logger.LogInformation(LogMessages.OutboundHttpRequest, request.Method, requestUri);
            HttpResponseMessage response = await httpClient.SendAsync(request);

            // Info-level inbound response (status only)
            m_Logger.LogInformation(LogMessages.InboundHttpResponse, (int)response.StatusCode, requestUri);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError(LogMessages.NonSuccessStatusCode, response.StatusCode, requestUri);
                return Result<IEnumerable<ZzzBasicAvatarData>>.Failure(StatusCode.ExternalServerError,
                    "Failed to retrieve character list data", requestUri);
            }

            ApiResponse<ZzzBasicAvatarResponse>? json =
                await JsonSerializer.DeserializeAsync<ApiResponse<ZzzBasicAvatarResponse>>(
                    await response.Content.ReadAsStreamAsync(), JsonOptions);

            if (json?.Data == null || json.Data.AvatarList.Count == 0)
            {
                m_Logger.LogError(LogMessages.FailedToParseResponse, requestUri, context.UserId);
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
                m_Logger.LogError(LogMessages.UnknownRetcode, json.Retcode, context.UserId, requestUri);
                return Result<IEnumerable<ZzzBasicAvatarData>>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later", requestUri);
            }

            m_Logger.LogInformation(LogMessages.SuccessfullyRetrievedData, requestUri, context.UserId);

            var cacheEntry = new CharacterListCacheEntry<ZzzBasicAvatarData>(cacheKey,
                json.Data.AvatarList, TimeSpan.FromMinutes(CacheExpirationMinutes));

            await m_Cache.SetAsync(cacheEntry);
            m_Logger.LogInformation(LogMessages.SuccessfullyCachedData, context.UserId, CacheExpirationMinutes);

            return Result<IEnumerable<ZzzBasicAvatarData>>.Success(json.Data.AvatarList, requestUri: requestUri);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, LogMessages.ExceptionOccurred,
                $"{HoYoLabDomains.PublicApi}{BasePath}/basic", context.UserId);
            return Result<IEnumerable<ZzzBasicAvatarData>>.Failure(StatusCode.BotError,
                "An error occurred while retrieving character data");
        }
    }

    public async Task<Result<ZzzFullAvatarData>> GetCharacterDetailAsync(CharacterApiContext context)
    {
        if (string.IsNullOrEmpty(context.Region) || string.IsNullOrEmpty(context.GameUid) || context.CharacterId == 0)
        {
            m_Logger.LogError(LogMessages.InvalidRegionOrUid);
            return Result<ZzzFullAvatarData>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        try
        {
            var requestUri =
                $"{HoYoLabDomains.PublicApi}{BasePath}/info?id_list[]={context.CharacterId}&server={context.Region}&role_id={context.GameUid}&need_wiki=true";

            m_Logger.LogInformation(LogMessages.ReceivedRequest, requestUri);

            HttpClient httpClient = m_HttpClientFactory.CreateClient("Default");

            HttpRequestMessage request = new()
            {
                RequestUri = new Uri(requestUri)
            };
            request.Headers.Add("Cookie", $"ltuid_v2={context.LtUid}; ltoken_v2={context.LToken}");
            request.Headers.Add("X-Rpc-Language", "en-us");

            // Info-level outbound request (no headers)
            m_Logger.LogInformation(LogMessages.OutboundHttpRequest, request.Method, requestUri);
            HttpResponseMessage response = await httpClient.SendAsync(request);

            // Info-level inbound response (status only)
            m_Logger.LogInformation(LogMessages.InboundHttpResponse, (int)response.StatusCode, requestUri);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError(LogMessages.NonSuccessStatusCode, response.StatusCode, requestUri);
                return Result<ZzzFullAvatarData>.Failure(StatusCode.ExternalServerError,
                    "Failed to retrieve character data");
            }

            ApiResponse<ZzzFullAvatarData>? json =
                await JsonSerializer.DeserializeAsync<ApiResponse<ZzzFullAvatarData>>(
                    await response.Content.ReadAsStreamAsync(), JsonOptions);

            if (json?.Data == null || json.Data.AvatarList.Count == 0)
            {
                m_Logger.LogError(LogMessages.FailedToParseResponse, requestUri, context.UserId);
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
                m_Logger.LogError(LogMessages.UnknownRetcode, json.Retcode, context.UserId, requestUri);
                return Result<ZzzFullAvatarData>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later");
            }

            m_Logger.LogInformation(LogMessages.SuccessfullyRetrievedData, requestUri, context.UserId);
            return Result<ZzzFullAvatarData>.Success(json.Data);
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
