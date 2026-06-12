using Mehrak.Domain.Character;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Mehrak.Infrastructure.Character.Services;

internal class PortraitUploadRateLimitService : IPortraitUploadRateLimitService
{
    private const int MaxUploads = 5;
    private static readonly TimeSpan Window = TimeSpan.FromHours(24);
    private const string KeyPrefix = "portrait_rate_limit:";

    private readonly IConnectionMultiplexer m_Redis;
    private readonly ILogger<PortraitUploadRateLimitService> m_Logger;

    public PortraitUploadRateLimitService(IConnectionMultiplexer redis, ILogger<PortraitUploadRateLimitService> logger)
    {
        m_Redis = redis;
        m_Logger = logger;
    }

    public async Task<bool> IsAllowedAsync(long discordUserId, CancellationToken ct = default)
    {
        var db = m_Redis.GetDatabase();
        var key = new RedisKey($"{KeyPrefix}{discordUserId}");
        var now = DateTimeOffset.UtcNow;
        var windowStart = now - Window;

        // Remove expired entries and count remaining
        var script = @"
            local key = KEYS[1]
            local window_start = tonumber(ARGV[1])
            local max = tonumber(ARGV[2])
            local now = ARGV[3]
            redis.call('ZREMRANGEBYSCORE', key, '-inf', window_start)
            local count = redis.call('ZCARD', key)
            if count < max then
                redis.call('ZADD', key, now, now)
                redis.call('EXPIRE', key, 86400)
                return 1
            end
            return 0";

        var result = await db.ScriptEvaluateAsync(
            script,
            [key],
            [(RedisValue)windowStart.ToUnixTimeMilliseconds().ToString(), MaxUploads, (RedisValue)now.ToUnixTimeMilliseconds().ToString()]);

        var allowed = (int)result == 1;

        if (!allowed)
        {
            m_Logger.LogWarning("Rate limit exceeded for portrait uploads by user {DiscordUserId}", discordUserId);
        }

        return allowed;
    }

    public async Task<int> GetRemainingAsync(long discordUserId, CancellationToken ct = default)
    {
        var db = m_Redis.GetDatabase();
        var key = new RedisKey($"{KeyPrefix}{discordUserId}");
        var windowStart = DateTimeOffset.UtcNow - Window;

        // Remove expired entries and count remaining
        var script = @"
            local key = KEYS[1]
            local window_start = tonumber(ARGV[1])
            redis.call('ZREMRANGEBYSCORE', key, '-inf', window_start)
            local count = redis.call('ZCARD', key)
            return count";

        var result = await db.ScriptEvaluateAsync(
            script,
            [key],
            [(RedisValue)windowStart.ToUnixTimeMilliseconds().ToString()]);

        var used = (int)result;
        return Math.Max(0, MaxUploads - used);
    }
}
