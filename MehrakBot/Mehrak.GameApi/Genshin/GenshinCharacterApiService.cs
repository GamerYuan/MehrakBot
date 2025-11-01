#region

using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Genshin.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.Genshin;

public class GenshinCharacterApiService : ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail,
    CharacterApiContext>
{
    private static readonly string BasePath = "/event/game_record/genshin/api";

    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<GenshinCharacterApiService> m_Logger;
    private readonly ICacheService m_Cache;
    private const int CacheExpirationMinutes = 10;

    public GenshinCharacterApiService(ICacheService cache,
        IHttpClientFactory httpClientFactory,
        ILogger<GenshinCharacterApiService> logger)
    {
        m_Cache = cache;
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<IEnumerable<GenshinBasicCharacterData>>> GetAllCharactersAsync(CharacterApiContext context)
    {
        if (string.IsNullOrEmpty(context.Region) || string.IsNullOrEmpty(context.GameUid))
        {
            m_Logger.LogError(LogMessages.InvalidRegionOrUid);
            return Result<IEnumerable<GenshinBasicCharacterData>>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        try
        {
            string cacheKey = $"genshin_characters_{context.GameUid}";
            var cachedEntry = await m_Cache.GetAsync<IEnumerable<GenshinBasicCharacterData>>(cacheKey);

            if (cachedEntry != null)
            {
                m_Logger.LogInformation(LogMessages.SuccessfullyRetrievedFromCache, context.GameUid);
                return Result<IEnumerable<GenshinBasicCharacterData>>.Success(cachedEntry);
            }

            var requestUri = $"{HoYoLabDomains.PublicApi}{BasePath}/character/list";

            m_Logger.LogInformation(LogMessages.ReceivedRequest, requestUri);

            var payload = new CharacterListPayload
            {
                RoleId = context.GameUid,
                Server = context.Region,
                SortType = 1
            };
            var httpClient = m_HttpClientFactory.CreateClient("Default");

            HttpRequestMessage request = new()
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri(requestUri),
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
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
                return Result<IEnumerable<GenshinBasicCharacterData>>.Failure(StatusCode.ExternalServerError,
                    "Failed to retrieve character list data", requestUri);
            }

            var json = await response.Content.ReadFromJsonAsync<ApiResponse<CharacterListData>>();

            if (json?.Data == null || json.Data.List.Count == 0)
            {
                m_Logger.LogError(LogMessages.FailedToParseResponse, requestUri, context.GameUid);
                return Result<IEnumerable<GenshinBasicCharacterData>>.Failure(StatusCode.ExternalServerError,
                    "Failed to retrieve character list data", requestUri);
            }

            // Info-level API retcode after parse
            m_Logger.LogInformation(LogMessages.InboundHttpResponseWithRetcode, (int)response.StatusCode, requestUri, json.Retcode, context.GameUid);

            if (json.Retcode == 10001)
            {
                m_Logger.LogError(LogMessages.InvalidCredentials, context.GameUid);
                return Result<IEnumerable<GenshinBasicCharacterData>>.Failure(StatusCode.Unauthorized,
                    "Invalid HoYoLAB UID or Cookies. Please authenticate again.", requestUri);
            }

            if (json.Retcode != 0)
            {
                m_Logger.LogError(LogMessages.UnknownRetcode, json.Retcode, context.GameUid, requestUri);
                return Result<IEnumerable<GenshinBasicCharacterData>>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later", requestUri);
            }

            m_Logger.LogInformation(LogMessages.SuccessfullyRetrievedData, requestUri, context.GameUid);

            await m_Cache.SetAsync(
                new CharacterListCacheEntry<GenshinBasicCharacterData>(cacheKey, json.Data.List,
                    TimeSpan.FromMinutes(CacheExpirationMinutes)));
            m_Logger.LogInformation(LogMessages.SuccessfullyCachedData, context.GameUid, CacheExpirationMinutes);

            return Result<IEnumerable<GenshinBasicCharacterData>>.Success(json.Data.List, requestUri: requestUri);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, LogMessages.ExceptionOccurred,
                $"{HoYoLabDomains.PublicApi}{BasePath}/character/list", context.GameUid);
            return Result<IEnumerable<GenshinBasicCharacterData>>.Failure(StatusCode.BotError,
                "An error occurred while retrieving character list data");
        }
    }

    public async Task<Result<GenshinCharacterDetail>> GetCharacterDetailAsync(CharacterApiContext context)
    {
        if (string.IsNullOrEmpty(context.Region) || string.IsNullOrEmpty(context.GameUid) || context.CharacterId == 0)
        {
            m_Logger.LogError(LogMessages.InvalidRegionOrUid);
            return Result<GenshinCharacterDetail>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        try
        {
            var requestUri = $"{HoYoLabDomains.PublicApi}{BasePath}/character/detail";

            m_Logger.LogInformation(LogMessages.ReceivedRequest, requestUri);

            var payload = new CharacterDetailPayload
            {
                RoleId = context.GameUid,
                Server = context.Region,
                CharacterIds = [context.CharacterId]
            };
            var httpClient = m_HttpClientFactory.CreateClient("Default");

            HttpRequestMessage request = new()
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri(requestUri),
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
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
                return Result<GenshinCharacterDetail>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while retrieving character data", requestUri);
            }

            ApiResponse<GenshinCharacterDetail>? json =
                await response.Content.ReadFromJsonAsync<ApiResponse<GenshinCharacterDetail>>();

            if (json == null || json.Data == null || json.Data.List.Count == 0)
            {
                m_Logger.LogError(LogMessages.FailedToParseResponse, requestUri, context.GameUid);
                return Result<GenshinCharacterDetail>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while retrieving character data", requestUri);
            }

            // Info-level API retcode after parse
            m_Logger.LogInformation(LogMessages.InboundHttpResponseWithRetcode, (int)response.StatusCode, requestUri, json.Retcode, context.GameUid);

            if (json?.Retcode == 10001)
            {
                m_Logger.LogError(LogMessages.InvalidCredentials, context.GameUid);
                return Result<GenshinCharacterDetail>.Failure(StatusCode.Unauthorized,
                    "Invalid HoYoLAB UID or Cookies. Please authenticate again.", requestUri);
            }

            if (json?.Retcode != 0)
            {
                m_Logger.LogError(LogMessages.UnknownRetcode, json?.Retcode, context.GameUid, requestUri);
                return Result<GenshinCharacterDetail>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later");
            }

            m_Logger.LogInformation(LogMessages.SuccessfullyRetrievedData, requestUri, context.GameUid);
            return Result<GenshinCharacterDetail>.Success(json!.Data, requestUri: requestUri);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, LogMessages.ExceptionOccurred,
                $"{HoYoLabDomains.PublicApi}{BasePath}/character/detail", context.GameUid);
            return Result<GenshinCharacterDetail>.Failure(StatusCode.BotError,
                "An error occurred while retrieving character data");
        }
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
