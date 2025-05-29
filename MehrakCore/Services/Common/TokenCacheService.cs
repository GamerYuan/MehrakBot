#region

using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

#endregion

namespace MehrakCore.Services.Common;

public class TokenCacheService
{
    private readonly IDistributedCache m_Cache;
    private readonly TimeSpan m_DefaultExpiration = TimeSpan.FromMinutes(5);
    private readonly ILogger<TokenCacheService> m_Logger;

    public TokenCacheService(IDistributedCache cache, ILogger<TokenCacheService> logger)
    {
        m_Cache = cache;
        m_Logger = logger;
        m_Logger.LogDebug("TokenCacheService initialized with default expiration of {ExpirationMinutes} minutes",
            m_DefaultExpiration.TotalMinutes);
    }

    public async Task AddCacheEntryAsync(ulong ltuid, string ltoken)
    {
        m_Logger.LogDebug("Adding cache entry with ltuid {LtUid}", ltuid);

        var options = new DistributedCacheEntryOptions()
            .SetAbsoluteExpiration(m_DefaultExpiration);

        await m_Cache.SetStringAsync($"TokenCache_{ltuid}", ltoken, options);
    }

    public async Task<string?> GetCacheEntry(ulong userId, ulong ltuid)
    {
        var ltoken = await m_Cache.GetStringAsync($"TokenCache_{ltuid}");
        m_Logger.LogDebug("Cache retrieval for user {UserId}, ltuid {Ltuid}: {Result}", userId, ltuid,
            ltoken != null ? "Found" : "Not Found");
        return ltoken;
    }
}