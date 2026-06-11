using Mehrak.Domain.Auth.Dtos;

namespace Mehrak.Domain.Auth;

public interface IDashboardUserService
{
    Task<IReadOnlyCollection<DashboardUserSummaryDto>> GetDashboardUsersAsync(CancellationToken ct = default);
    Task<DashboardUserSummaryDto?> GetDashboardUserByDiscordIdAsync(long discordUserId, CancellationToken ct = default);
    Task<AddDashboardUserResultDto> AddDashboardUserAsync(AddDashboardUserRequestDto request, CancellationToken ct = default);
    Task<UpdateDashboardUserResultDto> UpdateDashboardUserByDiscordIdAsync(UpdateDashboardUserRequestDto request, CancellationToken ct = default);
    Task<RemoveDashboardUserResultDto> RemoveDashboardUserByDiscordIdAsync(long discordUserId, CancellationToken ct = default);
}
