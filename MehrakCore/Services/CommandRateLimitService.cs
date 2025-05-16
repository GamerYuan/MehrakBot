#region

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#endregion

namespace MehrakCore.Services;

public class CommandRateLimitService
{
    private readonly IMemoryCache m_Cache;
    private readonly TimeSpan m_DefaultExpiration = TimeSpan.FromSeconds(10);
    private readonly ILogger<TokenCacheService> m_Logger;

    public CommandRateLimitService([FromKeyedServices("RateLimitCache")] IMemoryCache cache,
        ILogger<TokenCacheService> logger)
    {
        m_Cache = cache;
        m_Logger = logger;
        m_Logger.LogDebug("CommandRateLimitService initialized with default expiration: {Expiration}",
            m_DefaultExpiration);
    }

    public void SetRateLimit(ulong userId)
    {
        var options = new MemoryCacheEntryOptions().SetAbsoluteExpiration(m_DefaultExpiration);
        m_Logger.LogDebug("Setting rate limit for user {UserId}", userId);
        m_Cache.Set(userId.ToString(), true, options);
    }

    public bool IsRateLimited(ulong userId)
    {
        m_Logger.LogDebug("Checking rate limit for user {UserId}", userId);
        var isRateLimited = m_Cache.TryGetValue(userId.ToString(), out _);
        m_Logger.LogDebug("Rate limit check for user {UserId}: {IsRateLimited}", userId, isRateLimited);
        return isRateLimited;
    }
}
