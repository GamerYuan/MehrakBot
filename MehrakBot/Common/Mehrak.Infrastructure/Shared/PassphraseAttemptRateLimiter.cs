using StackExchange.Redis;

namespace Mehrak.Infrastructure.Shared;

public interface IPassphraseAttemptRateLimiter
{
    Task<bool> IsBlockedAsync(ulong discordUserId, CancellationToken ct = default);
    Task RecordFailureAsync(ulong discordUserId, CancellationToken ct = default);
    Task<int> GetRemainingAttemptsAsync(ulong discordUserId, CancellationToken ct = default);
}

internal class PassphraseAttemptRateLimiter : IPassphraseAttemptRateLimiter
{
    private const int MaxAttempts = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    private const string KeyPrefix = "passphrase_fail:";

    private readonly IConnectionMultiplexer m_Redis;

    public PassphraseAttemptRateLimiter(IConnectionMultiplexer redis)
    {
        m_Redis = redis;
    }

    public async Task<bool> IsBlockedAsync(ulong discordUserId, CancellationToken ct = default)
    {
        var db = m_Redis.GetDatabase();
        var key = new RedisKey($"{KeyPrefix}{discordUserId}");
        var now = DateTimeOffset.UtcNow;
        var windowStart = now - Window;

        var script = @"
            local key = KEYS[1]
            local window_start = tonumber(ARGV[1])
            local max = tonumber(ARGV[2])
            redis.call('ZREMRANGEBYSCORE', key, '-inf', window_start)
            local count = redis.call('ZCARD', key)
            if count >= max then
                return 1
            end
            return 0";

        var result = await db.ScriptEvaluateAsync(
            script,
            [key],
            [(RedisValue)windowStart.ToUnixTimeMilliseconds().ToString(), MaxAttempts]);

        return (int)result == 1;
    }

    public async Task RecordFailureAsync(ulong discordUserId, CancellationToken ct = default)
    {
        var db = m_Redis.GetDatabase();
        var key = new RedisKey($"{KeyPrefix}{discordUserId}");
        var now = DateTimeOffset.UtcNow;
        var windowStart = now - Window;

        var script = @"
            local key = KEYS[1]
            local window_start = tonumber(ARGV[1])
            local now = ARGV[2]
            redis.call('ZREMRANGEBYSCORE', key, '-inf', window_start)
            redis.call('ZADD', key, now, now)
            redis.call('EXPIRE', key, 900)";

        await db.ScriptEvaluateAsync(
            script,
            [key],
            [(RedisValue)windowStart.ToUnixTimeMilliseconds().ToString(), (RedisValue)now.ToUnixTimeMilliseconds().ToString()]);
    }

    public async Task<int> GetRemainingAttemptsAsync(ulong discordUserId, CancellationToken ct = default)
    {
        var db = m_Redis.GetDatabase();
        var key = new RedisKey($"{KeyPrefix}{discordUserId}");
        var now = DateTimeOffset.UtcNow;
        var windowStart = now - Window;

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
        return Math.Max(0, MaxAttempts - used);
    }
}
