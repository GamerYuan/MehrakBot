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

    // Test hook: fixed timestamps prove same-millisecond uploads are counted
    // individually via unique sorted-set members.
    internal Func<DateTimeOffset> UtcNowProvider { get; set; } = () => DateTimeOffset.UtcNow;

    public PortraitUploadRateLimitService(IConnectionMultiplexer redis, ILogger<PortraitUploadRateLimitService> logger)
    {
        m_Redis = redis;
        m_Logger = logger;
    }

    public async Task<bool> IsAllowedAsync(long discordUserId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var db = m_Redis.GetDatabase();
        var key = new RedisKey($"{KeyPrefix}{discordUserId}");
        var now = UtcNowProvider();
        var windowStart = now - Window;
        // Finding 12: every event gets a unique member while the score stays a
        // timestamp, so concurrent uploads in the same millisecond each count.
        // The check-and-add stays in one Lua script, atomically reserving the
        // slot before expensive upload work. Limits stay shared via Redis.
        var member = $"{now.ToUnixTimeMilliseconds()}:{Guid.NewGuid():N}";

        // Remove expired entries and count remaining
        var script = @"
            local key = KEYS[1]
            local window_start = tonumber(ARGV[1])
            local max = tonumber(ARGV[2])
            local now_score = tonumber(ARGV[3])
            local member = ARGV[4]
            redis.call('ZREMRANGEBYSCORE', key, '-inf', window_start)
            local count = redis.call('ZCARD', key)
            if count < max then
                redis.call('ZADD', key, now_score, member)
                redis.call('EXPIRE', key, 86400)
                return 1
            end
            return 0";

        var result = await db.ScriptEvaluateAsync(
            script,
            [key],
            [(RedisValue)windowStart.ToUnixTimeMilliseconds().ToString(),
                MaxUploads,
                (RedisValue)now.ToUnixTimeMilliseconds().ToString(),
                (RedisValue)member]);

        var allowed = (int)result == 1;

        if (!allowed)
        {
            m_Logger.LogWarning("Rate limit exceeded for portrait uploads by user {DiscordUserId}", discordUserId);
        }

        return allowed;
    }

    public async Task<int> GetRemainingAsync(long discordUserId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var db = m_Redis.GetDatabase();
        var key = new RedisKey($"{KeyPrefix}{discordUserId}");
        var windowStart = UtcNowProvider() - Window;

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
