using Mehrak.Domain.Auth.Dtos;

namespace Mehrak.Domain.Auth;

public interface IDashboardAuthService
{
    Task<LoginResultDto> LoginByDiscordAsync(long discordId, string discordUsername, CancellationToken ct = default);
    Task<bool> ValidateSessionAsync(string sessionToken, CancellationToken ct = default);
    Task InvalidateSessionAsync(string sessionToken, CancellationToken ct = default);
}
