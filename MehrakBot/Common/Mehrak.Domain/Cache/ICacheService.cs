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

    /// <summary>
    /// Cross-process unlock revocation watermark (unix milliseconds, plain
    /// non-sensitive value). Bot and Dashboard keep decrypted credentials in
    /// separate per-process memories, so logout and mass revocation publish
    /// the revocation instant here; Bot tickets issued at or before it are
    /// dropped on their next read. Never a credential: safe for shared Redis.
    /// </summary>
    public static string RevokeEpoch(ulong userId) => $"revoke-epoch:{userId}";
    public const string ReleaseNotes = "dashboard:release-notes";
}
