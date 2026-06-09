#region

using Mehrak.Domain.Cache.Abstractions;


#endregion

namespace Mehrak.Domain.Cache;

public interface ICacheService
{
    Task SetAsync<T>(ICacheEntry<T> entry, CancellationToken cancellationToken = default);

    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}

public static class CacheKeys
{
    public static string BotLToken(ulong userId, ulong ltUid) => $"bot:ltoken:{userId}:{ltUid}";
    public static string DashboardLToken(ulong userId, ulong ltUid) => $"dashboard:ltoken:{userId}:{ltUid}";
    public const string ReleaseNotes = "dashboard:release-notes";
}
