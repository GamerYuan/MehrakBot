﻿﻿#region

using System.Collections.Concurrent;
using System.Text.Json;
using Mehrak.Domain.Cache;
using Mehrak.Domain.Cache.Abstractions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.Infrastructure.Shared.Cache;

public class RedisCacheService : ICacheService
{
    private readonly IDistributedCache m_Cache;
    private readonly ILogger<RedisCacheService> m_Logger;

    // Decrypted HoYoLAB tokens must never reach durable Redis persistence (volume/AOF/backups). Credential keys are
    // kept in process memory only, with the same absolute TTL semantics as the Redis path. A best-effort purge removes
    // any pre-fix durable value so old plaintext becomes unreachable; reads never fall back to Redis for these keys.
    private readonly ConcurrentDictionary<string, SensitiveEntry> m_SensitiveCache = new();

    private sealed record SensitiveEntry(object? Value, DateTimeOffset ExpiresAt);

    public RedisCacheService(IDistributedCache cache, ILogger<RedisCacheService> logger)
    {
        m_Cache = cache;
        m_Logger = logger;
    }

    internal int SensitiveCount => m_SensitiveCache.Count;

    internal void PurgeExpiredSensitiveEntries()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in m_SensitiveCache)
        {
            if (pair.Value.ExpiresAt <= now)
                m_SensitiveCache.TryRemove(pair.Key, out _);
        }
    }

    internal static bool IsSensitiveKey(string key) =>
        key.StartsWith("bot:ltoken:", StringComparison.Ordinal) ||
        key.StartsWith("dashboard:ltoken:", StringComparison.Ordinal);

    public async Task SetAsync<T>(ICacheEntry<T> entry, CancellationToken cancellationToken = default)
    {
        m_Logger.LogDebug("Storing object with {Key} into cache", entry.Key);
        if (IsSensitiveKey(entry.Key))
        {
            // Expiry here is otherwise lazy (checked only when the same key is
            // read), so sweep expired entries on every write: without this,
            // untouched entries retain decrypted credentials for the process
            // lifetime and the dictionary grows without bound.
            PurgeExpiredSensitiveEntries();
            var expiresAt = DateTimeOffset.UtcNow.Add(entry.ExpirationTime);
            m_SensitiveCache[entry.Key] = new SensitiveEntry(entry.Value, expiresAt);
            // Purge any pre-fix durable value; never fail the write if Redis is down.
            try
            {
                await m_Cache.RemoveAsync(entry.Key, cancellationToken);
            }
            catch (Exception ex)
            {
                m_Logger.LogDebug(ex, "Best-effort purge of durable credential entry failed for {Key}", entry.Key);
            }

            return;
        }

        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = entry.ExpirationTime
        };
        await m_Cache.SetStringAsync(entry.Key, JsonSerializer.Serialize(entry.Value), options, cancellationToken);
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        m_Logger.LogDebug("Retrieving object with {Key} from cache", key);
        if (IsSensitiveKey(key))
        {
            if (!m_SensitiveCache.TryGetValue(key, out var stored))
                return default;

            if (stored.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                m_SensitiveCache.TryRemove(key, out _);
                return default;
            }

            // Fail closed on type mismatch (for example a legacy plaintext
            // string read as a ticket): drop the entry and require fresh auth.
            if (stored.Value is null)
                return default;

            if (stored.Value is T typed)
                return typed;

            m_SensitiveCache.TryRemove(key, out _);
            m_Logger.LogDebug("Dropping credential cache entry with unexpected shape for {Key}", key);
            return default;
        }

        var val = await m_Cache.GetStringAsync(key, cancellationToken);
        if (string.IsNullOrEmpty(val)) return default;

        return JsonSerializer.Deserialize<T>(val);
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        m_Logger.LogDebug("Removing object with {Key} from cache", key);
        if (IsSensitiveKey(key))
        {
            m_SensitiveCache.TryRemove(key, out _);
            try
            {
                await m_Cache.RemoveAsync(key, cancellationToken);
            }
            catch (Exception ex)
            {
                m_Logger.LogDebug(ex, "Best-effort purge of durable credential entry failed for {Key}", key);
            }

            return;
        }

        await m_Cache.RemoveAsync(key, cancellationToken);
    }
}


