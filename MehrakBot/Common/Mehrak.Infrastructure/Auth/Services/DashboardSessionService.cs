using System.Text.Json;
using Mehrak.Domain.Auth;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Mehrak.Infrastructure.Auth.Services;

public class DashboardSessionService : IDashboardSessionService
{
    private const string KeyPrefix = "dashboard_session:";
    private static readonly TimeSpan SessionTtl = TimeSpan.FromDays(7);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IConnectionMultiplexer m_Redis;
    private readonly ILogger<DashboardSessionService> m_Logger;

    public DashboardSessionService(IConnectionMultiplexer redis, ILogger<DashboardSessionService> logger)
    {
        m_Redis = redis;
        m_Logger = logger;
    }

    public async Task CreateSessionAsync(string sessionToken, long discordUserId, string? accessToken, CancellationToken ct = default)
    {
        var db = m_Redis.GetDatabase();
        var key = new RedisKey($"{KeyPrefix}{sessionToken}");

        var data = new SessionData(discordUserId, accessToken, DateTime.UtcNow);
        var json = JsonSerializer.Serialize(data, JsonOptions);

        await db.StringSetAsync(key, json, SessionTtl);

        // Also store a reverse mapping for invalidation by user
        var userKey = new RedisKey($"dashboard_user_sessions:{discordUserId}");
        await db.SetAddAsync(userKey, sessionToken);
        await db.KeyExpireAsync(userKey, SessionTtl);

        m_Logger.LogInformation("Session created for DiscordId {DiscordUserId}", discordUserId);
    }

    public async Task<DashboardSessionData?> GetSessionAsync(string sessionToken, CancellationToken ct = default)
    {
        var db = m_Redis.GetDatabase();
        var key = new RedisKey($"{KeyPrefix}{sessionToken}");

        var json = await db.StringGetAsync(key);
        if (json.IsNullOrEmpty)
            return null;

        var data = JsonSerializer.Deserialize<SessionData>((string)json!, JsonOptions);
        if (data == null)
            return null;

        return new DashboardSessionData(data.DiscordUserId, data.AccessToken, data.LastTokenValidation);
    }

    public async Task RefreshSessionAsync(string sessionToken, CancellationToken ct = default)
    {
        var db = m_Redis.GetDatabase();
        var key = new RedisKey($"{KeyPrefix}{sessionToken}");

        await db.KeyExpireAsync(key, SessionTtl);
    }

    public async Task InvalidateSessionAsync(string sessionToken, CancellationToken ct = default)
    {
        var db = m_Redis.GetDatabase();
        var key = new RedisKey($"{KeyPrefix}{sessionToken}");

        // Get the session data to find the user for reverse mapping cleanup
        var json = await db.StringGetAsync(key);
        if (!json.IsNullOrEmpty)
        {
            var data = JsonSerializer.Deserialize<SessionData>((string)json!, JsonOptions);
            if (data != null)
            {
                var userKey = new RedisKey($"dashboard_user_sessions:{data.DiscordUserId}");
                await db.SetRemoveAsync(userKey, sessionToken);
            }
        }

        await db.KeyDeleteAsync(key);
        m_Logger.LogInformation("Session invalidated: {Token}", sessionToken[..6]);
    }

    public async Task InvalidateAllForUserAsync(long discordUserId, CancellationToken ct = default)
    {
        var db = m_Redis.GetDatabase();
        var userKey = new RedisKey($"dashboard_user_sessions:{discordUserId}");

        var tokens = await db.SetMembersAsync(userKey);
        foreach (var token in tokens)
        {
            if (!token.IsNullOrEmpty)
            {
                var sessionKey = new RedisKey($"{KeyPrefix}{token}");
                await db.KeyDeleteAsync(sessionKey);
            }
        }

        await db.KeyDeleteAsync(userKey);
        m_Logger.LogInformation("All sessions invalidated for DiscordId {DiscordUserId}", discordUserId);
    }

    public async Task<bool> NeedsTokenValidationAsync(string sessionToken, CancellationToken ct = default)
    {
        var session = await GetSessionAsync(sessionToken, ct);
        if (session == null)
            return false;

        return session.LastTokenValidation.Date < DateTime.UtcNow.Date;
    }

    public async Task MarkTokenValidatedAsync(string sessionToken, CancellationToken ct = default)
    {
        var db = m_Redis.GetDatabase();
        var key = new RedisKey($"{KeyPrefix}{sessionToken}");

        var json = await db.StringGetAsync(key);
        if (json.IsNullOrEmpty)
            return;

        var data = JsonSerializer.Deserialize<SessionData>((string)json!, JsonOptions);
        if (data == null)
            return;

        var updated = new SessionData(data.DiscordUserId, data.AccessToken, DateTime.UtcNow);
        var updatedJson = JsonSerializer.Serialize(updated, JsonOptions);

        await db.StringSetAsync(key, updatedJson, SessionTtl);
    }

    public async Task<bool> TryClaimTokenValidationAsync(string sessionToken, CancellationToken ct = default)
    {
        var db = m_Redis.GetDatabase();
        var key = new RedisKey($"{KeyPrefix}{sessionToken}");
        var today = DateTime.UtcNow.Date;

        // Lua script: atomically check if validation is needed and claim it by updating the timestamp.
        // Returns 1 if the claim succeeded (caller should validate), 0 otherwise.
        const string script = @"
            local json = redis.call('GET', KEYS[1])
            if not json then return 0 end
            local data = cjson.decode(json)
            local last = data['lastTokenValidation']
            if not last then
                data['lastTokenValidation'] = ARGV[1]
                redis.call('SET', KEYS[1], cjson.encode(data), 'EX', ARGV[2])
                return 1
            end
            local lastDate = string.sub(last, 1, 10)
            if lastDate < ARGV[3] then
                data['lastTokenValidation'] = ARGV[1]
                redis.call('SET', KEYS[1], cjson.encode(data), 'EX', ARGV[2])
                return 1
            end
            return 0";

        var now = DateTime.UtcNow;
        var result = await db.ScriptEvaluateAsync(
            script,
            [key],
            [
                (RedisValue)now.ToString("O"),
                (RedisValue)((int)SessionTtl.TotalSeconds).ToString(),
                (RedisValue)today.ToString("yyyy-MM-dd")
            ]);

        return (int)result == 1;
    }

    private sealed record SessionData(long DiscordUserId, string? AccessToken, DateTime LastTokenValidation);
}
