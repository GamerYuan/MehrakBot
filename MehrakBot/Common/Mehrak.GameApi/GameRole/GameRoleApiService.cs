#region

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mehrak.Domain.Cache;
using Mehrak.Domain.Shared.Abstractions;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.Domain.User.Models;
using Mehrak.GameApi.Shared;
using Mehrak.GameApi.Shared.Types;
using Mehrak.Infrastructure.Shared.Cache;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.GameRole;

public class GameRoleApiService : IApiService<GameProfileDto, GameRoleApiContext>
{
    private static readonly string GameUserRoleApiPath =
        "/binding/api/getUserGameRolesByLtoken";

    private static readonly MemoryCache LockCache = new(new MemoryCacheOptions());

    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<GameRoleApiService> m_Logger;
    private readonly ICacheService m_CacheService;

    public GameRoleApiService(IHttpClientFactory httpClientFactory, ICacheService cacheService, ILogger<GameRoleApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_CacheService = cacheService;
        m_Logger = logger;
    }

    public async Task<Result<GameProfileDto>> GetAsync(GameRoleApiContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(IApiService.MaxTimeoutSeconds));

            var cacheKey = $"gameProfile:{context.UserId}:{context.LtUid}";

            var cachedData = await m_CacheService.GetAsync<string>(cacheKey, timeoutCts.Token);
            if (!string.IsNullOrEmpty(cachedData))
            {
                var dto = TryDeserializeAndMap(cachedData, context);
                if (dto != null)
                    return Result<GameProfileDto>.Success(dto);
            }

            var semaphore = GetOrCreateLock(cacheKey);
            await semaphore.WaitAsync(timeoutCts.Token);

            try
            {
                cachedData = await m_CacheService.GetAsync<string>(cacheKey, timeoutCts.Token);
                if (!string.IsNullOrEmpty(cachedData))
                {
                    var dto = TryDeserializeAndMap(cachedData, context);
                    if (dto != null)
                        return Result<GameProfileDto>.Success(dto);
                }

                return await FetchAndCacheGameRoleAsync(cacheKey, context, timeoutCts.Token);
            }
            finally
            {
                semaphore.Release();
            }
        }
        catch (OperationCanceledException)
        {
            return Result<GameProfileDto>.FromCancellation(cancellationToken);
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, LogMessages.ExceptionOccurred,
                $"{HoYoLabDomains.AccountApi}{GameUserRoleApiPath}", context.UserId);
            return Result<GameProfileDto>.Failure(StatusCode.BotError,
                "An error occurred while processing the request");
        }
    }

    private static SemaphoreSlim GetOrCreateLock(string cacheKey)
    {
        return LockCache.GetOrCreate(cacheKey, entry =>
        {
            entry.SlidingExpiration = TimeSpan.FromMinutes(10);
            return new SemaphoreSlim(1, 1);
        })!;
    }

    private GameProfileDto? TryDeserializeAndMap(string cachedData, GameRoleApiContext context)
    {
        try
        {
            var cachedJson = JsonSerializer.Deserialize<ApiResponse<GameProfileResponse>>(cachedData);
            var cachedProfile = cachedJson?.Data?.List
                .FirstOrDefault(x => x.GameBiz == context.Game.ToGameBizString() && x.Region == context.Region);

            if (cachedProfile == null)
                return null;

            var dto = MapToGameProfileDto(cachedProfile);
            if (dto == null)
                return null;

            return dto;
        }
        catch (JsonException ex)
        {
            m_Logger.LogWarning(ex, "Failed to deserialize cached game profile data for user {UserId}. Falling back to API fetch.", context.UserId);
            return null;
        }
    }

    private async Task<Result<GameProfileDto>> FetchAndCacheGameRoleAsync(string cacheKey, GameRoleApiContext context,
        CancellationToken cancellationToken = default)
    {
        var requestUri = $"{HoYoLabDomains.AccountApi}{GameUserRoleApiPath}";

        m_Logger.LogInformation(LogMessages.PreparingRequest, requestUri);

        var httpClient = m_HttpClientFactory.CreateClient("Default");
        HttpRequestMessage request = new()
        {
            Method = HttpMethod.Get,
            RequestUri = new Uri(requestUri)
        };
        request.Headers.Add("Cookie", $"ltoken_v2={context.LToken}; ltuid_v2={context.LtUid}");

        var response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            m_Logger.LogError(LogMessages.NonSuccessStatusCode, response.StatusCode, requestUri);
            return Result<GameProfileDto>.Failure(StatusCode.ExternalServerError, "API returned error status code", requestUri);
        }

        var json = await response.Content.ReadFromJsonAsync<ApiResponse<GameProfileResponse>>(cancellationToken);

        if (json == null)
        {
            m_Logger.LogError(LogMessages.EmptyResponseData, requestUri, context.UserId);
            return Result<GameProfileDto>.Failure(StatusCode.ExternalServerError,
                "An error occurred while retrieving profile information", requestUri);
        }

        if (json.Retcode == -100)
        {
            m_Logger.LogError(LogMessages.InvalidCredentials, context.UserId);
            return Result<GameProfileDto>.Failure(StatusCode.Unauthorized,
                "Invalid HoYoLAB UID or Cookies. Please re-authenticate", requestUri);
        }

        if (json.Retcode != 0)
        {
            m_Logger.LogError(LogMessages.UnknownRetcode, json.Retcode, context.UserId, requestUri, json);
            return Result<GameProfileDto>.Failure(StatusCode.ExternalServerError,
                $"An error occurred while retrieving profile information", requestUri);
        }

        if (json.Data?.List == null || json.Data?.List.Count == 0)
        {
            m_Logger.LogWarning("No game data found for User {UserId} profile LtUid {LtUid}",
                context.UserId, context.LtUid);
            return Result<GameProfileDto>.Failure(StatusCode.ExternalServerError,
                "No game information found. Please use the correct account", requestUri);
        }

        await m_CacheService.SetAsync(new CacheEntryBase<string>(cacheKey,
            JsonSerializer.Serialize(json), TimeSpan.FromMinutes(10)), cancellationToken);

        // Info-level API retcode after parse (success path)
        m_Logger.LogInformation(LogMessages.InboundHttpResponseWithRetcode, (int)response.StatusCode, requestUri, 0, context.UserId);

        var gameProfile = json?.Data?.List.FirstOrDefault(x => x.GameBiz == context.Game.ToGameBizString() && x.Region == context.Region);

        if (gameProfile == null)
        {
            m_Logger.LogWarning("No matching game profile found for User {UserId}, Game {Game}, Region {Region}",
                context.UserId, context.Game, context.Region);
            return Result<GameProfileDto>.Failure(StatusCode.ExternalServerError,
                "No matching game information found. Please ensure you have the correct account and game region", requestUri);
        }

        var dto = MapToGameProfileDto(gameProfile);

        if (dto == null)
        {
            m_Logger.LogWarning("Game profile data is incomplete for User {UserId}, Game {Game}, Region {Region}",
                context.UserId, context.Game, context.Region);
            return Result<GameProfileDto>.Failure(StatusCode.ExternalServerError,
                "Incomplete game information received. Please try again later", requestUri);
        }

        return Result<GameProfileDto>.Success(dto, requestUri: requestUri);
    }

    private static GameProfileDto? MapToGameProfileDto(GameProfile? profile)
    {
        if (profile == null)
            return null;

        if (string.IsNullOrEmpty(profile.GameUid) || string.IsNullOrEmpty(profile.Nickname) || profile.Level == null)
        {
            return null;
        }

        return new GameProfileDto
        {
            GameUid = profile.GameUid,
            Nickname = profile.Nickname,
            Level = profile.Level.Value
        };
    }

    private sealed class GameProfileResponse
    {
        [JsonPropertyName("list")] public List<GameProfile> List { get; set; } = [];
    }

    private sealed class GameProfile
    {
        [JsonPropertyName("game_biz")] public string? GameBiz { get; init; }

        [JsonPropertyName("region")] public string? Region { get; init; }

        [JsonPropertyName("game_uid")] public string? GameUid { get; init; }

        [JsonPropertyName("nickname")] public string? Nickname { get; init; }

        [JsonPropertyName("level")] public int? Level { get; init; }

        [JsonPropertyName("is_chosen")] public bool? IsChosen { get; init; }

        [JsonPropertyName("region_name")] public string? RegionName { get; init; }

        [JsonPropertyName("is_official")] public bool? IsOfficial { get; init; }
    }
}

public class GameRoleApiContext : IApiContext
{
    public ulong UserId { get; }
    public ulong LtUid { get; }
    public string LToken { get; }
    public Game Game { get; }
    public string Region { get; }

    public GameRoleApiContext(ulong userId, ulong ltuid, string ltoken, Game game, string region)
    {
        UserId = userId;
        LtUid = ltuid;
        LToken = ltoken;
        Game = game;
        Region = region;
    }
}
