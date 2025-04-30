// Modify TokenCacheService.cs

#region

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

#endregion

public class TokenCacheService
{
    private readonly IMemoryCache m_Cache;
    private readonly TimeSpan m_DefaultExpiration = TimeSpan.FromMinutes(5);
    private readonly ILogger<TokenCacheService> m_Logger;

    public TokenCacheService(IMemoryCache cache, ILogger<TokenCacheService> logger)
    {
        m_Cache = cache;
        m_Logger = logger;
    }

    public void AddCacheEntry(ulong userId, ulong ltuid, string ltoken)
    {
        var options = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(m_DefaultExpiration);

        m_Logger.LogDebug("Adding cache entry for user {UserId}", userId);
        m_Cache.Set($"ltoken_{userId}", ltoken, options);
        m_Cache.Set($"ltuid_{userId}", ltuid, options);
    }

    public bool TryGetToken(ulong userId, out string ltoken)
    {
        return m_Cache.TryGetValue($"ltoken_{userId}", out ltoken);
    }

    public bool TryGetLtUid(ulong userId, out ulong ltuid)
    {
        return m_Cache.TryGetValue($"ltuid_{userId}", out ltuid);
    }

    public void RemoveEntry(ulong userId)
    {
        m_Logger.LogDebug("Removing cache entry for user {UserId}", userId);
        m_Cache.Remove($"ltoken_{userId}");
        m_Cache.Remove($"ltuid_{userId}");
    }
}
