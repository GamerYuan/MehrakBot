namespace Mehrak.Domain.Auth;

public record DashboardSessionData(
    long DiscordUserId,
    string? AccessToken,
    DateTime LastTokenValidation,
    string? LoginIp,
    string? UserAgent,
    string? Location);

public interface IDashboardSessionService
{
    Task CreateSessionAsync(string sessionToken, long discordUserId, string? accessToken, string? loginIp, string? userAgent, string? location, CancellationToken ct = default);
    Task<DashboardSessionData?> GetSessionAsync(string sessionToken, CancellationToken ct = default);
    Task RefreshSessionAsync(string sessionToken, CancellationToken ct = default);
    Task InvalidateSessionAsync(string sessionToken, CancellationToken ct = default);
    Task InvalidateAllForUserAsync(long discordUserId, CancellationToken ct = default);
    Task<bool> TryClaimTokenValidationAsync(string sessionToken, CancellationToken ct = default);
    Task<int> CleanupExpiredSessionsAsync(CancellationToken ct = default);
}
