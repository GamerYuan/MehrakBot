#region

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#endregion

namespace MehrakCore.Services;

public class TokenCacheService
{
    private readonly IMemoryCache m_Cache;
    private readonly TimeSpan m_DefaultExpiration = TimeSpan.FromMinutes(5);
    private readonly ILogger<TokenCacheService> m_Logger;

    public TokenCacheService([FromKeyedServices("TokenCache")] IMemoryCache cache, ILogger<TokenCacheService> logger)
    {
        m_Cache = cache;
        m_Logger = logger;
        m_Logger.LogDebug("TokenCacheService initialized with default expiration of {ExpirationMinutes} minutes",
            m_DefaultExpiration.TotalMinutes);
    }

    public void AddCacheEntry(ulong userId, ulong ltuid, string ltoken)
    {
        var options = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(m_DefaultExpiration)
            .RegisterPostEvictionCallback((key, value, reason, state) =>
            {
                m_Logger.LogDebug("Token cache entry {Key} evicted due to {Reason}", key, reason);
            });

        m_Logger.LogDebug("Adding token cache entry for user {UserId} with expiration {Expiration}",
            userId, DateTime.UtcNow.Add(m_DefaultExpiration));

        m_Cache.Set($"ltoken_{userId}", ltoken, options);
        m_Cache.Set($"ltuid_{userId}", ltuid, options);
    }

    public bool TryGetToken(ulong userId, out string? ltoken)
    {
        var result = m_Cache.TryGetValue($"ltoken_{userId}", out ltoken);
        m_Logger.LogDebug("Token retrieval for user {UserId}: {Result}", userId, result ? "Found" : "Not Found");
        return result;
    }

    public bool TryGetLtUid(ulong userId, out ulong ltuid)
    {
        var result = m_Cache.TryGetValue($"ltuid_{userId}", out ltuid);
        m_Logger.LogDebug("LtUid retrieval for user {UserId}: {Result}", userId, result ? "Found" : "Not Found");
        return result;
    }

    public void RemoveEntry(ulong userId)
    {
        m_Cache.Remove($"ltoken_{userId}");
        m_Cache.Remove($"ltuid_{userId}");
        m_Logger.LogDebug("Removed token cache entries for user {UserId}", userId);
    }
}
