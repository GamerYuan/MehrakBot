using Mehrak.Domain.Auth;
using Mehrak.Domain.Auth.Dtos;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Infrastructure.Auth.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Mehrak.Infrastructure.Auth.Services;

public class DashboardUserService : IDashboardUserService
{
    private readonly DashboardAuthDbContext m_Db;
    private readonly ILogger<DashboardUserService> m_Logger;

    public DashboardUserService(DashboardAuthDbContext db, ILogger<DashboardUserService> logger)
    {
        m_Db = db;
        m_Logger = logger;
    }

    public async Task<IReadOnlyCollection<DashboardUserSummaryDto>> GetDashboardUsersAsync(CancellationToken ct = default)
    {
        m_Logger.LogInformation("Fetching dashboard user summaries.");
        var users = await m_Db.DashboardUsers
            .Include(u => u.GamePermissions)
            .Where(u => u.IsSuperAdmin || u.GamePermissions.Any(p => p.AllowWrite))
            .OrderBy(u => u.Username)
            .ToListAsync(ct);

        return [.. users
            .Select(u => new DashboardUserSummaryDto
            {
                UserId = u.Id,
                Username = u.Username,
                DiscordUserId = u.DiscordId.ToString(),
                IsSuperAdmin = u.IsSuperAdmin,
                IsRootUser = u.IsRootUser,
                GameWritePermissions = [.. u.GamePermissions
                    .Where(p => p.AllowWrite)
                    .Select(p => Enum.TryParse<Game>(p.GameCode, true, out var g) ? g : Game.Unsupported)
                    .Where(g => g != Game.Unsupported)
                    .Distinct()]
            })];
    }

    public async Task<DashboardUserSummaryDto?> GetDashboardUserByDiscordIdAsync(long discordUserId, CancellationToken ct = default)
    {
        m_Logger.LogInformation("Fetching dashboard user summary for DiscordId {DiscordUserId}.", discordUserId);
        var user = await m_Db.DashboardUsers
            .Include(u => u.GamePermissions)
            .SingleOrDefaultAsync(u => u.DiscordId == discordUserId, ct);
        if (user == null)
            return null;
        return ToSummaryDto(user);
    }

    public async Task<AddDashboardUserResultDto> AddDashboardUserAsync(AddDashboardUserRequestDto request, CancellationToken ct = default)
    {
        if (request.DiscordUserId <= 0)
        {
            return new AddDashboardUserResultDto
            {
                Succeeded = false,
                Error = "Discord user id must be a positive number."
            };
        }

        if (await m_Db.DashboardUsers.AnyAsync(u => u.DiscordId == request.DiscordUserId, ct))
        {
            return new AddDashboardUserResultDto
            {
                Succeeded = false,
                Error = "Discord user id is already in use."
            };
        }

        var normalizedPermissions = (request.GameWritePermissions ?? [])
            .Select(g => g.ToString().ToLowerInvariant())
            .Distinct()
            .ToArray();

        var user = new DashboardUser
        {
            Username = string.Empty,
            DiscordId = request.DiscordUserId,
            IsSuperAdmin = false,
            IsActive = true,
            UpdatedAtUtc = DateTime.UtcNow
        };

        user.GamePermissions = normalizedPermissions
            .Select(code => new DashboardGamePermission
            {
                GameCode = code,
                AllowWrite = true,
                User = user
            })
            .ToList();

        m_Db.DashboardUsers.Add(user);

        try
        {
            await m_Db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e)
        {
            m_Logger.LogError(e, "Failed to create dashboard user due to a database error.");
            return new AddDashboardUserResultDto
            {
                Succeeded = false,
                Error = "Failed to create user due to a database error."
            };
        }

        m_Logger.LogInformation("Dashboard user {UserId} created with DiscordId {DiscordId}.", user.Id, user.DiscordId);

        return new AddDashboardUserResultDto
        {
            Succeeded = true,
            UserId = user.Id,
            Username = user.Username,
            GameWritePermissions = [.. request.GameWritePermissions ?? []]
        };
    }

    public async Task<UpdateDashboardUserResultDto> UpdateDashboardUserByDiscordIdAsync(UpdateDashboardUserRequestDto request, CancellationToken ct = default)
    {
        m_Logger.LogInformation("Updating dashboard user {DiscordUserId}.", request.DiscordUserId);
        var user = await m_Db.DashboardUsers.Include(x => x.GamePermissions)
            .SingleOrDefaultAsync(x => x.DiscordId == request.DiscordUserId, ct);
        if (user == null)
        {
            return new UpdateDashboardUserResultDto
            {
                Succeeded = false,
                Error = "User not found."
            };
        }

        if (user.IsRootUser)
        {
            return new UpdateDashboardUserResultDto
            {
                Succeeded = false,
                Error = "Root user cannot be modified."
            };
        }

        var normalizedPermissions = (request.GameWritePermissions ?? [])
            .Select(g => g.ToString().ToLowerInvariant())
            .Distinct()
            .ToArray();

        user.IsSuperAdmin = request.IsSuperAdmin;
        user.IsActive = request.IsActive;
        user.UpdatedAtUtc = DateTime.UtcNow;

        SyncGamePermissions(user, normalizedPermissions);

        try
        {
            await m_Db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e)
        {
            m_Logger.LogError(e, "Failed to update dashboard user due to a database error.");
            return new UpdateDashboardUserResultDto
            {
                Succeeded = false,
                Error = "Failed to update user due to a database error."
            };
        }

        m_Logger.LogInformation("Dashboard user {UserId} updated successfully.", user.Id);

        return new UpdateDashboardUserResultDto
        {
            Succeeded = true,
            Username = user.Username,
            IsActive = user.IsActive,
            IsSuperAdmin = user.IsSuperAdmin,
            IsRootUser = user.IsRootUser,
            GameWritePermissions = [.. request.GameWritePermissions ?? []]
        };
    }

    public async Task<RemoveDashboardUserResultDto> RemoveDashboardUserByDiscordIdAsync(long discordUserId, CancellationToken ct = default)
    {
        m_Logger.LogInformation("Deleting dashboard user with DiscordId {DiscordUserId}.", discordUserId);
        var user = await m_Db.DashboardUsers.SingleOrDefaultAsync(u => u.DiscordId == discordUserId, ct);
        if (user == null)
        {
            return new RemoveDashboardUserResultDto
            {
                Succeeded = false,
                Error = "User not found."
            };
        }

        if (user.IsRootUser)
        {
            return new RemoveDashboardUserResultDto
            {
                Succeeded = false,
                Error = "Root user cannot be deleted."
            };
        }

        m_Db.DashboardUsers.Remove(user);

        try
        {
            await m_Db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e)
        {
            m_Logger.LogError(e, "Failed to delete dashboard user due to a database error.");
            return new RemoveDashboardUserResultDto
            {
                Succeeded = false,
                Error = "Failed to delete user due to a database error."
            };
        }

        m_Logger.LogInformation("Dashboard user with DiscordId {DiscordUserId} deleted.", discordUserId);

        return new RemoveDashboardUserResultDto
        {
            Succeeded = true
        };
    }

    private static DashboardUserSummaryDto ToSummaryDto(DashboardUser user)
    {
        return new DashboardUserSummaryDto
        {
            UserId = user.Id,
            Username = user.Username,
            DiscordUserId = user.DiscordId.ToString(),
            IsSuperAdmin = user.IsSuperAdmin,
            IsRootUser = user.IsRootUser,
            GameWritePermissions = [.. user.GamePermissions
                .Where(p => p.AllowWrite)
                .Select(p => Enum.TryParse<Game>(p.GameCode, true, out var g) ? g : Game.Unsupported)
                .Where(g => g != Game.Unsupported)
                .Distinct()]
        };
    }

    private void SyncGamePermissions(DashboardUser user, string[] normalizedPermissions)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        var toRemove = user.GamePermissions
            .Where(permission => !normalizedPermissions.Contains(permission.GameCode, comparer))
            .ToList();

        if (toRemove.Count > 0)
        {
            foreach (var permission in toRemove)
                m_Db.DashboardGamePermissions.Remove(permission);
        }

        var existingCodes = user.GamePermissions
            .Select(p => p.GameCode)
            .Where(code => code != null)
            .ToHashSet(comparer);

        var addedCount = 0;
        foreach (var code in normalizedPermissions.Where(code => !existingCodes.Contains(code)))
        {
            m_Db.DashboardGamePermissions.Add(new DashboardGamePermission
            {
                UserId = user.Id,
                User = user,
                GameCode = code,
                AllowWrite = true
            });
            existingCodes.Add(code);
            addedCount++;
        }

        m_Logger.LogDebug("Synchronized permissions for user {UserId}. Added: {Added}, Removed: {Removed}.",
            user.Id, addedCount, toRemove.Count);
    }
}
