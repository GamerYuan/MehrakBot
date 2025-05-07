#region

using System.Collections.Concurrent;
using MehrakCore.ApiResponseTypes.Genshin;
using Microsoft.Extensions.Logging;

#endregion

namespace MehrakCore.Services;

public class PaginationCacheService<T> where T : IBasicCharacterData
{
    private readonly ConcurrentDictionary<ulong, (IEnumerable<T> Items, string gameUid, string
            region)>
        m_Cache = new();

    private readonly TimeSpan m_ExpirationTime = TimeSpan.FromMinutes(10);
    private readonly ILogger<PaginationCacheService<T>> m_Logger;

    public PaginationCacheService(ILogger<PaginationCacheService<T>> logger)
    {
        m_Logger = logger;
        m_Logger.LogInformation(
            "PaginationCacheService initialized with expiration time of {ExpirationMinutes} minutes",
            m_ExpirationTime.TotalMinutes);
    }

    public void StoreItems(ulong userId, IEnumerable<T> items, string gameUid, string region)
    {
        var itemsList = items.ToList();
        m_Cache[userId] = (itemsList, gameUid, region);
        m_Logger.LogInformation("Stored {ItemCount} items for user {UserId} in pagination cache",
            itemsList.Count, userId);
    }

    public IEnumerable<T> GetItems(ulong userId)
    {
        if (!m_Cache.TryGetValue(userId, out var entry))
        {
            m_Logger.LogWarning("Cache miss: No items found for user {UserId}", userId);
            return [];
        }

        m_Logger.LogDebug("Retrieved {ItemCount} items from cache for user {UserId}",
            entry.Items.Count(), userId);
        return entry.Items;
    }

    public IEnumerable<T> GetPageItems(ulong userId, int page, int pageSize = 25)
    {
        m_Logger.LogDebug("Retrieving page {Page} (size: {PageSize}) for user {UserId}",
            page, pageSize, userId);

        var items = GetItems(userId);
        if (!items.Any())
        {
            m_Logger.LogWarning("No items available for pagination for user {UserId}", userId);
            return [];
        }

        var startIndex = page * pageSize;
        var result = items
            .Skip(startIndex)
            .Take(pageSize)
            .ToList();

        m_Logger.LogDebug("Retrieved {ItemCount} items for page {Page} for user {UserId}",
            result.Count, page, userId);
        return result;
    }

    public string GetGameUid(ulong userId)
    {
        if (m_Cache.TryGetValue(userId, out var entry))
        {
            m_Logger.LogDebug("Retrieved game UID for user {UserId}: {GameUid}", userId, entry.gameUid);
            return entry.gameUid;
        }

        m_Logger.LogWarning("No game UID found for user {UserId}", userId);
        return string.Empty;
    }

    public string GetRegion(ulong userId)
    {
        if (m_Cache.TryGetValue(userId, out var entry))
        {
            m_Logger.LogDebug("Retrieved region for user {UserId}: {Region}", userId, entry.region);
            return entry.region;
        }

        m_Logger.LogWarning("No region found for user {UserId}", userId);
        return string.Empty;
    }

    public void RemoveEntry(ulong userId)
    {
        if (m_Cache.TryRemove(userId, out _))
        {
            m_Logger.LogInformation("Removed pagination cache entry for user {UserId}", userId);
            return;
        }

        m_Logger.LogWarning("No cache entry found for user {UserId} to remove", userId);
    }
}
