using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Mehrak.Infrastructure.Services;

public class CommandRateLimitService
{
    private readonly IDistributedCache m_Cache;
    private readonly TimeSpan m_DefaultExpiration = TimeSpan.FromSeconds(10);
    private readonly ILogger<CommandRateLimitService> m_Logger;

    public CommandRateLimitService(IDistributedCache cache, ILogger<CommandRateLimitService> logger)
    {
        m_Cache = cache;
        m_Logger = logger;
        m_Logger.LogDebug(
            "CommandRateLimitService initialized with default expiration: {Expiration}",
            m_DefaultExpiration);
    }

    public async Task SetRateLimitAsync(ulong userId)
    {
        var options = new DistributedCacheEntryOptions().SetAbsoluteExpiration(m_DefaultExpiration);
        m_Logger.LogDebug("Setting rate limit for user {UserId}", userId);
        await m_Cache.SetStringAsync($"RateLimit_{userId}", "true", options);
    }

    public async Task<bool> IsRateLimitedAsync(ulong userId)
    {
        m_Logger.LogDebug("Checking rate limit for user {UserId}", userId);
        var value = await m_Cache.GetStringAsync($"RateLimit_{userId}");
        var isRateLimited = value != null;
        m_Logger.LogDebug("Rate limit check for user {UserId}: {IsRateLimited}", userId, isRateLimited);
        return isRateLimited;
    }
}