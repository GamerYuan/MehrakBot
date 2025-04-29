// Modify TokenCacheService.cs

#region

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

#endregion

public class TokenCacheService
{
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _defaultExpiration = TimeSpan.FromMinutes(30); // Increased from 1 minute
    private readonly ILogger<TokenCacheService> _logger;

    public TokenCacheService(IMemoryCache cache, ILogger<TokenCacheService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public void AddCacheEntry(ulong userId, ulong ltuid, string ltoken)
    {
        var options = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(_defaultExpiration);

        _logger.LogDebug("Adding cache entry for user {UserId}", userId);
        _cache.Set($"ltoken_{userId}", ltoken, options);
        _cache.Set($"ltuid_{userId}", ltuid, options);
    }

    public bool TryGetToken(ulong userId, out string ltoken)
    {
        return _cache.TryGetValue($"ltoken_{userId}", out ltoken);
    }

    public bool TryGetLtUid(ulong userId, out ulong ltuid)
    {
        return _cache.TryGetValue($"ltuid_{userId}", out ltuid);
    }

    public void RemoveEntry(ulong userId)
    {
        _logger.LogDebug("Removing cache entry for user {UserId}", userId);
        _cache.Remove($"ltoken_{userId}");
        _cache.Remove($"ltuid_{userId}");
    }
}
