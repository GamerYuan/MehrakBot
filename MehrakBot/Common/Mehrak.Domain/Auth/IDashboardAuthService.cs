using Mehrak.Domain.Auth.Dtos;

namespace Mehrak.Domain.Auth;

public interface IDashboardAuthService
{
    Task<LoginResultDto> LoginByDiscordAsync(long discordId, string discordUsername, string? avatarHash, string? accessToken, string? loginIp, string? userAgent, string? location, CancellationToken ct = default);
    Task InvalidateSessionAsync(string sessionToken, CancellationToken ct = default);
    Task<string?> GetAccessTokenAsync(string sessionToken, CancellationToken ct = default);
}
