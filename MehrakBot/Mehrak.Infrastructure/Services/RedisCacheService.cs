using Mehrak.Domain.Models.Abstractions;
using Mehrak.Domain.Services.Abstractions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Mehrak.Infrastructure.Services;

public class RedisCacheService : ICacheService
{
    private readonly IDistributedCache m_Cache;
    private readonly ILogger<RedisCacheService> m_Logger;

    public RedisCacheService(IDistributedCache cache, ILogger<RedisCacheService> logger)
    {
        m_Cache = cache;
        m_Logger = logger;
    }

    public async Task SetAsync<T>(ICacheEntry<T> entry)
    {
        m_Logger.LogDebug("Storing object with {Key} into cache", entry.Key);
        var options = new DistributedCacheEntryOptions()
        {
            AbsoluteExpirationRelativeToNow = entry.ExpirationTime
        };
        await m_Cache.SetStringAsync(entry.Key, JsonSerializer.Serialize(entry.Value), options);
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        m_Logger.LogDebug("Retrieving object with {Key} from cache", key);
        var val = await m_Cache.GetStringAsync(key);
        if (string.IsNullOrEmpty(val))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(val);
    }

    public async Task RemoveAsync(string key)
    {
        m_Logger.LogDebug("Removing object with {Key} from cache", key);
        await m_Cache.RemoveAsync(key);
    }
}
