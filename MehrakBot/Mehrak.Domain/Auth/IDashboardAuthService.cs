using Mehrak.Domain.Auth.Dtos;

namespace Mehrak.Domain.Auth;

public interface IDashboardAuthService
{
    Task<LoginResultDto> LoginAsync(LoginRequestDto request, CancellationToken ct = default);
    Task<bool> ValidateSessionAsync(string sessionToken, CancellationToken ct = default);
    Task InvalidateSessionAsync(string sessionToken, CancellationToken ct = default);
}
