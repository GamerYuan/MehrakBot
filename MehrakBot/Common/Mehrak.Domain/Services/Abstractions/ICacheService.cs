#region

using Mehrak.Domain.Models.Abstractions;

#endregion

namespace Mehrak.Domain.Services.Abstractions;

public interface ICacheService
{
    Task SetAsync<T>(ICacheEntry<T> entry);

    Task<T?> GetAsync<T>(string key);

    Task RemoveAsync(string key);
}

public static class CacheKeys
{
    public static string BotLToken(ulong userId, ulong ltUid) => $"bot:ltoken:{userId}:{ltUid}";
    public static string DashboardLToken(ulong userId, ulong ltUid) => $"dashboard:ltoken:{userId}:{ltUid}";
}
