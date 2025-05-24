#region

using System.Diagnostics.CodeAnalysis;
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

    public void AddCacheEntry(ulong ltuid, string ltoken)
    {
        m_Logger.LogDebug("Adding cache entry with ltuid {LtUid}", ltuid);

        var options = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(m_DefaultExpiration)
            .RegisterPostEvictionCallback((key, _, reason, _) =>
            {
                m_Logger.LogDebug("Cache entry {Key} evicted due to {Reason}", key, reason);
            });

        m_Cache.Set(ltuid, ltoken, options);
    }

    public bool TryGetCacheEntry(ulong userId, ulong ltuid, [NotNullWhen(true)] out string? ltoken)
    {
        var result = m_Cache.TryGetValue(ltuid, out ltoken);
        m_Logger.LogDebug("Cache retrieval for user {UserId}: {Result}", userId, result ? "Found" : "Not Found");
        return result;
    }
}
