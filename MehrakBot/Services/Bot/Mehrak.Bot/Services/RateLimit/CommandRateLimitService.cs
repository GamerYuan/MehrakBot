#region

using Mehrak.Infrastructure.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

#endregion

namespace Mehrak.Bot.Services.RateLimit;

internal class CommandRateLimitService : ICommandRateLimitService
{
    private readonly IDatabase m_Redis;
    private readonly ILogger<CommandRateLimitService> m_Logger;
    private readonly RateLimiterConfig m_Config;

    private readonly string m_InstanceName;

    private const string GCRAScript = @"
        local key = KEYS[1]
        local now = tonumber(ARGV[1])
        local leak_interval = tonumber(ARGV[2])
        local burst_offset = tonumber(ARGV[3])

        -- Get the Theoretical Arrival Time (TAT)
        local tat = tonumber(redis.call('GET', key))

        if not tat then
            tat = now
        end

        -- Calculate the new TAT
        local new_tat = math.max(now, tat) + leak_interval

        -- If the new TAT is too far in the future, the bucket is full
        if new_tat <= (now + burst_offset) then
            redis.call('SET', key, new_tat, 'PX', burst_offset + leak_interval)
            return 1
        else
            return 0
        end";

    public CommandRateLimitService(IOptions<RedisConfig> redisConfig,
        IOptions<RateLimiterConfig> rateLimitConfig,
        IConnectionMultiplexer redisConnection,
        ILogger<CommandRateLimitService> logger)
    {
        m_InstanceName = redisConfig.Value.InstanceName;
        m_Redis = redisConnection.GetDatabase();
        m_Config = rateLimitConfig.Value;
        m_Logger = logger;
    }

    public async Task<bool> IsAllowedAsync(ulong userId)
    {
        var key = $"{m_InstanceName}cmd_rate_limit:{userId}";

        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var leakMs = (long)m_Config.LeakInterval.TotalMilliseconds;
        var burstOffsetMs = leakMs * m_Config.Capacity;

        var result = await m_Redis.ScriptEvaluateAsync(
            GCRAScript,
            keys: [(RedisKey)key],
            values: [nowMs, leakMs, burstOffsetMs]);

        var allowed = (int)result == 1;
        m_Logger.LogDebug("User {UserId} is allowed: {Allowed}", userId, allowed);

        return allowed;
    }
}

public class RateLimiterConfig
{
    public TimeSpan LeakInterval { get; set; } = TimeSpan.FromSeconds(30);
    public int Capacity { get; set; } = 10;
}
