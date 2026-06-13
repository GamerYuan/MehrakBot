namespace Mehrak.Domain.Auth;

public record DashboardSessionData(long DiscordUserId, string? AccessToken, DateTime LastTokenValidation);

public interface IDashboardSessionService
{
    Task CreateSessionAsync(string sessionToken, long discordUserId, string? accessToken, CancellationToken ct = default);
    Task<DashboardSessionData?> GetSessionAsync(string sessionToken, CancellationToken ct = default);
    Task RefreshSessionAsync(string sessionToken, CancellationToken ct = default);
    Task InvalidateSessionAsync(string sessionToken, CancellationToken ct = default);
    Task InvalidateAllForUserAsync(long discordUserId, CancellationToken ct = default);
    Task<bool> NeedsTokenValidationAsync(string sessionToken, CancellationToken ct = default);
    Task MarkTokenValidatedAsync(string sessionToken, CancellationToken ct = default);
}
