using Mehrak.Domain.Auth.Dtos;

namespace Mehrak.Domain.Auth;

public interface IDashboardUserService
{
    Task<IReadOnlyCollection<DashboardUserSummaryDto>> GetDashboardUsersAsync(CancellationToken ct = default);
    Task<DashboardUserSummaryDto?> GetDashboardUserByIdAsync(Guid userId, CancellationToken ct = default);
    Task<AddDashboardUserResultDto> AddDashboardUserAsync(AddDashboardUserRequestDto request, CancellationToken ct = default);
    Task<UpdateDashboardUserResultDto> UpdateDashboardUserAsync(UpdateDashboardUserRequestDto request, CancellationToken ct = default);
    Task<RemoveDashboardUserResultDto> RemoveDashboardUserAsync(Guid userId, CancellationToken ct = default);
    Task<DashboardUserRequireResetResultDto> RequirePasswordResetAsync(Guid userId, CancellationToken ct = default);
}
