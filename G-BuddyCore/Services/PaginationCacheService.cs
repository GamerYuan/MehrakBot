#region

using System.Collections.Concurrent;
using G_BuddyCore.ApiResponseTypes.Genshin;
using Microsoft.Extensions.Logging;

#endregion

namespace G_BuddyCore.Services;

public class PaginationCacheService
{
    private readonly ConcurrentDictionary<ulong, (IEnumerable<BasicCharacterData> Items, DateTime LastAccessed)>
        m_Cache = new();

    private readonly TimeSpan m_ExpirationTime = TimeSpan.FromMinutes(10);
    private readonly ILogger<PaginationCacheService> m_Logger;

    public PaginationCacheService(ILogger<PaginationCacheService> logger)
    {
        m_Logger = logger;
        m_Logger.LogInformation(
            "PaginationCacheService initialized with expiration time of {ExpirationMinutes} minutes",
            m_ExpirationTime.TotalMinutes);
    }

    public void StoreItems(ulong userId, IEnumerable<BasicCharacterData> items)
    {
        var itemsList = items.ToList();
        m_Cache[userId] = (itemsList, DateTime.UtcNow);
        m_Logger.LogInformation("Stored {ItemCount} items for user {UserId} in pagination cache",
            itemsList.Count, userId);
    }

    public IEnumerable<BasicCharacterData> GetItems(ulong userId)
    {
        if (!m_Cache.TryGetValue(userId, out var entry))
        {
            m_Logger.LogWarning("Cache miss: No items found for user {UserId}", userId);
            return [];
        }

        if (DateTime.UtcNow - entry.LastAccessed > m_ExpirationTime)
        {
            m_Logger.LogInformation("Cache expired for user {UserId} (last accessed: {LastAccessed})",
                userId, entry.LastAccessed);
            m_Cache.TryRemove(userId, out _);
            return [];
        }

        // Update last accessed time
        m_Cache[userId] = (entry.Items, DateTime.UtcNow);
        m_Logger.LogDebug("Retrieved {ItemCount} items from cache for user {UserId}",
            entry.Items.Count(), userId);
        return entry.Items;
    }

    public IEnumerable<BasicCharacterData> GetPageItems(ulong userId, int page, int pageSize = 25)
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

    public int GetTotalPages(ulong userId, int pageSize = 25)
    {
        var items = GetItems(userId);
        var itemCount = items.Count();
        var totalPages = (int)Math.Ceiling(itemCount / (double)pageSize);

        m_Logger.LogDebug(
            "Calculated {TotalPages} total pages for user {UserId} ({ItemCount} items, {PageSize} per page)",
            totalPages, userId, itemCount, pageSize);

        return totalPages;
    }
}
