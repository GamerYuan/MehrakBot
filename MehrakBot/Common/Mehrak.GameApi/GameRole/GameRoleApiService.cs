#region

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
        var requestUri = $"{HoYoLabDomains.AccountApi}{GameUserRoleApiPath}";

        try
        {
            using var scope = new HoYoLabRequestScope(cancellationToken);

            var cacheKey = $"gameProfile:{context.UserId}:{context.LtUid}";

            var cachedData = await m_CacheService.GetAsync<string>(cacheKey, scope.Token);
            if (!string.IsNullOrEmpty(cachedData))
            {
                var dto = TryDeserializeAndMap(cachedData, context);
                if (dto != null)
                    return Result<GameProfileDto>.Success(dto);
            }

            var semaphore = GetOrCreateLock(cacheKey);
            await semaphore.WaitAsync(scope.Token);

            try
            {
                cachedData = await m_CacheService.GetAsync<string>(cacheKey, scope.Token);
                if (!string.IsNullOrEmpty(cachedData))
                {
                    var dto = TryDeserializeAndMap(cachedData, context);
                    if (dto != null)
                        return Result<GameProfileDto>.Success(dto);
                }

                return await FetchAndCacheGameRoleAsync(cacheKey, context, scope.Token);
            }
            finally
            {
                semaphore.Release();
            }
        }
        catch (OperationCanceledException)
        {
            return Result<GameProfileDto>.FromCancellation(cancellationToken, requestUri);
        }
        catch (Exception ex)
        {
            GameApiLog.LogException(m_Logger, $"{HoYoLabDomains.AccountApi}{GameUserRoleApiPath}", context.UserId, ex);
            return Result<GameProfileDto>.Failure(StatusCode.BotError,
                "An error occurred while processing the request", requestUri);
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
            m_Logger.LogWarning("Failed to deserialize cached game profile data for user {UserId}: {ExceptionType}. Falling back to API fetch.", GameApiLog.ContextId(context.UserId), GameApiLog.ExceptionType(ex));
            return null;
        }
    }

    private async Task<Result<GameProfileDto>> FetchAndCacheGameRoleAsync(string cacheKey, GameRoleApiContext context,
        CancellationToken cancellationToken = default)
    {
        var requestUri = $"{HoYoLabDomains.AccountApi}{GameUserRoleApiPath}";

        var envelope = await HoYoLabApi.SendEnvelopeAsync<GameProfileResponse>(
            CreateRequest(requestUri),
            new HoYoLabCredentials(context.UserId, context.LtUid, context.LToken),
            m_HttpClientFactory, m_Logger, cancellationToken, HoYoLabRetcodes.AccountAuth);

        if (!envelope.IsSuccess)
            return Result<GameProfileDto>.Failure(envelope.StatusCode, envelope.ErrorMessage, envelope.RequestUri);

        var json = envelope.Data;

        if (json.Retcode == HoYoLabRetcodes.AccountAuth)
        {
            m_Logger.LogError(LogMessages.InvalidCredentials, GameApiLog.ContextId(context.UserId));
            return Result<GameProfileDto>.Failure(StatusCode.Unauthorized,
                HoYoLabFailureMessages.Default.Unauthorized, requestUri);
        }

        if (json.Retcode != 0)
        {
            m_Logger.LogError(LogMessages.UnknownRetcode, json.Retcode, GameApiLog.ContextId(context.UserId),
                GameApiLog.Endpoint(requestUri), GameApiLog.SafeQueryParameters(requestUri),
                GameApiLog.RenderBody(json));
            return Result<GameProfileDto>.Failure(StatusCode.ExternalServerError,
                "An error occurred while retrieving profile information", requestUri);
        }

        if (json.Data?.List == null || json.Data?.List.Count == 0)
        {
            m_Logger.LogWarning("No game data found for User {UserId}", GameApiLog.ContextId(context.UserId));
            return Result<GameProfileDto>.Failure(StatusCode.ExternalServerError,
                "No game information found. Please use the correct account", requestUri);
        }

        await m_CacheService.SetAsync(new CacheEntryBase<string>(cacheKey,
            JsonSerializer.Serialize(json), TimeSpan.FromMinutes(10)), cancellationToken);

        var gameProfile = json?.Data?.List.FirstOrDefault(x => x.GameBiz == context.Game.ToGameBizString() && x.Region == context.Region);

        if (gameProfile == null)
        {
            m_Logger.LogWarning("No matching game profile found for User {UserId}, Game {Game}, Region {Region}",
                GameApiLog.ContextId(context.UserId), context.Game, context.Region);
            return Result<GameProfileDto>.Failure(StatusCode.ExternalServerError,
                "No matching game information found. Please ensure you have the correct account and game region", requestUri);
        }

        var dto = MapToGameProfileDto(gameProfile);

        if (dto == null)
        {
            m_Logger.LogWarning("Game profile data is incomplete for User {UserId}, Game {Game}, Region {Region}",
                GameApiLog.ContextId(context.UserId), context.Game, context.Region);
            return Result<GameProfileDto>.Failure(StatusCode.ExternalServerError,
                "Incomplete game information received. Please try again later", requestUri);
        }

        return Result<GameProfileDto>.Success(dto, requestUri: requestUri);
    }

    public async Task<Result<List<GameRoleInfo>>> GetAllGameProfilesAsync(ulong userId, ulong ltuid, string ltoken,
        CancellationToken cancellationToken = default, bool bypassCache = false)
    {
        var requestUri = $"{HoYoLabDomains.AccountApi}{GameUserRoleApiPath}";

        try
        {
            using var scope = new HoYoLabRequestScope(cancellationToken);

            var cacheKey = $"gameProfile:{userId}:{ltuid}";

            if (!bypassCache)
            {
                var cachedData = await m_CacheService.GetAsync<string>(cacheKey, scope.Token);
                if (!string.IsNullOrEmpty(cachedData))
                {
                    var profiles = TryDeserializeAll(cachedData);
                    if (profiles.Count > 0)
                        return Result<List<GameRoleInfo>>.Success(profiles);
                }
            }

            var semaphore = GetOrCreateLock(cacheKey);
            await semaphore.WaitAsync(scope.Token);

            try
            {
                if (!bypassCache)
                {
                    var cachedData = await m_CacheService.GetAsync<string>(cacheKey, scope.Token);
                    if (!string.IsNullOrEmpty(cachedData))
                    {
                        var profiles = TryDeserializeAll(cachedData);
                        if (profiles.Count > 0)
                            return Result<List<GameRoleInfo>>.Success(profiles);
                    }
                }

                return await FetchAndCacheAllGameRolesAsync(cacheKey, userId, ltuid, ltoken, scope.Token);
            }
            finally
            {
                semaphore.Release();
            }
        }
        catch (OperationCanceledException)
        {
            return Result<List<GameRoleInfo>>.FromCancellation(cancellationToken, requestUri);
        }
        catch (Exception ex)
        {
            GameApiLog.LogException(m_Logger, $"{HoYoLabDomains.AccountApi}{GameUserRoleApiPath}", userId, ex);
            return Result<List<GameRoleInfo>>.Failure(StatusCode.BotError,
                "An error occurred while processing the request", requestUri);
        }
    }

    private List<GameRoleInfo> TryDeserializeAll(string cachedData)
    {
        try
        {
            var cachedJson = JsonSerializer.Deserialize<ApiResponse<GameProfileResponse>>(cachedData);
            return cachedJson?.Data?.List
                .Select(p => (Game: GameEnumExtensions.FromGameBizString(p.GameBiz ?? ""), Region: p.Region, Profile: MapToGameProfileDto(p)))
                .Where(x => x.Game != Game.Unsupported && !string.IsNullOrEmpty(x.Region))
                .Select(x => new GameRoleInfo(x.Game, x.Game.RegionToServerString(x.Region!), x.Profile!))
                .Where(x => x.Profile != null)
                .ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task<Result<List<GameRoleInfo>>> FetchAndCacheAllGameRolesAsync(
        string cacheKey, ulong userId, ulong ltuid, string ltoken, CancellationToken cancellationToken)
    {
        var requestUri = $"{HoYoLabDomains.AccountApi}{GameUserRoleApiPath}";

        var envelope = await HoYoLabApi.SendEnvelopeAsync<GameProfileResponse>(
            CreateRequest(requestUri),
            new HoYoLabCredentials(userId, ltuid, ltoken),
            m_HttpClientFactory, m_Logger, cancellationToken, HoYoLabRetcodes.AccountAuth);

        if (!envelope.IsSuccess)
            return Result<List<GameRoleInfo>>.Failure(envelope.StatusCode, envelope.ErrorMessage, envelope.RequestUri);

        var json = envelope.Data;

        if (json.Retcode == HoYoLabRetcodes.AccountAuth)
        {
            m_Logger.LogError(LogMessages.InvalidCredentials, GameApiLog.ContextId(userId));
            return Result<List<GameRoleInfo>>.Failure(StatusCode.Unauthorized,
                HoYoLabFailureMessages.Default.Unauthorized, requestUri);
        }

        if (json.Retcode != 0)
        {
            m_Logger.LogError(LogMessages.UnknownRetcode, json.Retcode, GameApiLog.ContextId(userId),
                GameApiLog.Endpoint(requestUri), GameApiLog.SafeQueryParameters(requestUri),
                GameApiLog.RenderBody(json));
            return Result<List<GameRoleInfo>>.Failure(StatusCode.ExternalServerError,
                "An error occurred while retrieving profile information", requestUri);
        }

        if (json.Data?.List == null || json.Data.List.Count == 0)
        {
            m_Logger.LogWarning("No game data found for User {UserId}", GameApiLog.ContextId(userId));
            return Result<List<GameRoleInfo>>.Failure(StatusCode.ExternalServerError,
                "No game information found. Please use the correct account", requestUri);
        }

        // Cache raw JSON so subsequent GetAsync calls benefit
        await m_CacheService.SetAsync(new CacheEntryBase<string>(cacheKey,
            JsonSerializer.Serialize(json), TimeSpan.FromMinutes(10)), cancellationToken);

        var result = json.Data.List
            .Select(p => (Game: GameEnumExtensions.FromGameBizString(p.GameBiz ?? ""), Region: p.Region, Profile: MapToGameProfileDto(p)))
            .Where(x => x.Game != Game.Unsupported && !string.IsNullOrEmpty(x.Region))
            .Select(x => new GameRoleInfo(x.Game, x.Game.RegionToServerString(x.Region!), x.Profile!))
            .Where(x => x.Profile != null)
            .ToList();

        if (result.Count == 0)
        {
            m_Logger.LogWarning("No supported game profiles found for User {UserId}", GameApiLog.ContextId(userId));
            return Result<List<GameRoleInfo>>.Failure(StatusCode.ExternalServerError,
                "No supported game information found. Please use the correct account", requestUri);
        }

        return Result<List<GameRoleInfo>>.Success(result, requestUri: requestUri);
    }

    private static HoYoLabRequest CreateRequest(string requestUri)
    {
        return new HoYoLabRequest
        {
            RequestUri = requestUri,
            CookieFormat = "ltoken_v2={1}; ltuid_v2={0}"
        };
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

        [JsonPropertyName("game_uid")][GameApiSensitive] public string? GameUid { get; init; }

        [JsonPropertyName("nickname")][GameApiSensitive] public string? Nickname { get; init; }

        [JsonPropertyName("level")] public int? Level { get; init; }

        [JsonPropertyName("is_chosen")] public bool? IsChosen { get; init; }

        [JsonPropertyName("region_name")] public string? RegionName { get; init; }

        [JsonPropertyName("is_official")] public bool? IsOfficial { get; init; }
    }
}

public record GameRoleInfo(Game Game, string Region, GameProfileDto Profile);

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
