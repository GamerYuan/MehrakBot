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

    private readonly MemoryCacheEntryOptions m_Options;

    public TokenCacheService([FromKeyedServices("TokenCache")] IMemoryCache cache, ILogger<TokenCacheService> logger)
    {
        m_Cache = cache;
        m_Logger = logger;
        m_Logger.LogDebug("TokenCacheService initialized with default expiration of {ExpirationMinutes} minutes",
            m_DefaultExpiration.TotalMinutes);

        m_Options = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(m_DefaultExpiration)
            .RegisterPostEvictionCallback((key, value, reason, state) =>
            {
                m_Logger.LogDebug("Cache entry {Key} evicted due to {Reason}", key, reason);
            });
    }

    public void AddCacheEntry(ulong ltuid, string ltoken)
    {
        m_Logger.LogDebug("Adding cache entry with ltuid {LtUid}", ltuid);

        m_Cache.Set(ltuid, ltoken, m_Options);
    }

    public bool TryGetCacheEntry(ulong userId, ulong ltuid, out string? ltoken)
    {
        var result = m_Cache.TryGetValue(ltuid, out ltoken);
        m_Logger.LogDebug("Cache retrieval for user {UserId}: {Result}", userId, result ? "Found" : "Not Found");
        return result;
    }
}
