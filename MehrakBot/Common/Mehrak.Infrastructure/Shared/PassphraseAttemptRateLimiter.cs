﻿﻿using StackExchange.Redis;

namespace Mehrak.Infrastructure.Shared;

public interface IPassphraseAttemptRateLimiter
{
    Task<bool> IsBlockedAsync(ulong discordUserId, CancellationToken ct = default);
    Task RecordFailureAsync(ulong discordUserId, CancellationToken ct = default);
    Task<int> GetRemainingAttemptsAsync(ulong discordUserId, CancellationToken ct = default);

    /// <summary>
    /// Atomically reserves one attempt slot before expensive passphrase work. Returns a reservation id when the attempt
    /// is allowed, or null when the caller is rate limited. The reservation counts toward the quota until released:
    /// wrong-passphrase outcomes keep it as the failure record, while successes and non-passphrase failures must
    /// release it. Limits stay shared across instances via Redis. </summary>
    Task<string?> TryReserveAttemptAsync(ulong discordUserId, CancellationToken ct = default);

    /// <summary>
    /// Releases a reservation created by <see cref="TryReserveAttemptAsync"/>.
    /// Best-effort: never throws for Redis failures.
    /// </summary>
    Task ReleaseReservationAsync(ulong discordUserId, string reservationId, CancellationToken ct = default);
}

internal class PassphraseAttemptRateLimiter : IPassphraseAttemptRateLimiter
{
    private const int MaxAttempts = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    private const int WindowSeconds = 900;
    private const string KeyPrefix = "passphrase_fail:";

    private readonly IConnectionMultiplexer m_Redis;

    // Test hook: fixed timestamps prove same-millisecond events are counted
    // individually via unique sorted-set members.
    internal Func<DateTimeOffset> UtcNowProvider { get; set; } = () => DateTimeOffset.UtcNow;

    public PassphraseAttemptRateLimiter(IConnectionMultiplexer redis)
    {
        m_Redis = redis;
    }

    public async Task<bool> IsBlockedAsync(ulong discordUserId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var db = m_Redis.GetDatabase();
        var key = new RedisKey($"{KeyPrefix}{discordUserId}");
        var now = UtcNowProvider();
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
        ct.ThrowIfCancellationRequested();
        var db = m_Redis.GetDatabase();
        var key = new RedisKey($"{KeyPrefix}{discordUserId}");
        var now = UtcNowProvider();
        var windowStart = now - Window;
        // Unique member per event: concurrent failures in the same millisecond
        // must each increase the count. The score stays a timestamp for windowing.
        var member = $"{now.ToUnixTimeMilliseconds()}:{Guid.NewGuid():N}";

        var script = @"
            local key = KEYS[1]
            local window_start = tonumber(ARGV[1])
            local now_score = tonumber(ARGV[2])
            local member = ARGV[3]
            redis.call('ZREMRANGEBYSCORE', key, '-inf', window_start)
            redis.call('ZADD', key, now_score, member)
            redis.call('EXPIRE', key, 900)";

        await db.ScriptEvaluateAsync(
            script,
            [key],
            [(RedisValue)windowStart.ToUnixTimeMilliseconds().ToString(),
                (RedisValue)now.ToUnixTimeMilliseconds().ToString(),
                (RedisValue)member]);
    }

    public async Task<string?> TryReserveAttemptAsync(ulong discordUserId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var db = m_Redis.GetDatabase();
        var key = new RedisKey($"{KeyPrefix}{discordUserId}");
        var now = UtcNowProvider();
        var windowStart = now - Window;
        var member = $"{now.ToUnixTimeMilliseconds()}:{Guid.NewGuid():N}";

        var script = @"
            local key = KEYS[1]
            local window_start = tonumber(ARGV[1])
            local now_score = tonumber(ARGV[2])
            local member = ARGV[3]
            local max = tonumber(ARGV[4])
            local ttl = tonumber(ARGV[5])
            redis.call('ZREMRANGEBYSCORE', key, '-inf', window_start)
            local count = redis.call('ZCARD', key)
            if count >= max then
                return nil
            end
            redis.call('ZADD', key, now_score, member)
            redis.call('EXPIRE', key, ttl)
            return member";

        var result = await db.ScriptEvaluateAsync(
            script,
            [key],
            [(RedisValue)windowStart.ToUnixTimeMilliseconds().ToString(),
                (RedisValue)now.ToUnixTimeMilliseconds().ToString(),
                (RedisValue)member,
                (RedisValue)MaxAttempts,
                (RedisValue)WindowSeconds]);

        if (result.IsNull)
            return null;

        return (string?)result;
    }

    public async Task ReleaseReservationAsync(ulong discordUserId, string reservationId, CancellationToken ct = default)
    {
        try
        {
            // Best-effort cleanup must survive caller cancellation: callers pass
            // the request token, and a disconnected client must not turn a
            // successful attempt into a phantom failure for the full window.
            // The removal itself runs uncancelled; other failures are swallowed.
            var db = m_Redis.GetDatabase();
            var key = new RedisKey($"{KeyPrefix}{discordUserId}");

            const string script = @"
                redis.call('ZREM', KEYS[1], ARGV[1])
                return 1";

            await db.ScriptEvaluateAsync(script, [key], [(RedisValue)reservationId], flags: CommandFlags.None);
        }
        catch (Exception)
        {
            // Best-effort cleanup: a leaked reservation expires with the window.
        }
    }

    public async Task<int> GetRemainingAttemptsAsync(ulong discordUserId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var db = m_Redis.GetDatabase();
        var key = new RedisKey($"{KeyPrefix}{discordUserId}");
        var now = UtcNowProvider();
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


