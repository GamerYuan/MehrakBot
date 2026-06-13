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
    private readonly IDashboardSessionService m_SessionService;
    private readonly ILogger<DashboardUserService> m_Logger;

    public DashboardUserService(DashboardAuthDbContext db, IDashboardSessionService sessionService, ILogger<DashboardUserService> logger)
    {
        m_Db = db;
        m_SessionService = sessionService;
        m_Logger = logger;
    }

    public async Task<IReadOnlyCollection<DashboardUserSummaryDto>> GetDashboardUsersAsync(CancellationToken ct = default)
    {
        m_Logger.LogInformation("Fetching dashboard user summaries.");

        // Get all unique DiscordIds that have permissions
        var discordIds = await m_Db.DashboardPermissions
            .Select(p => p.DiscordId)
            .Distinct()
            .ToListAsync(ct);

        var result = new List<DashboardUserSummaryDto>();

        foreach (var discordId in discordIds)
        {
            var permissions = await m_Db.DashboardPermissions
                .Where(p => p.DiscordId == discordId)
                .Select(p => p.Permission)
                .ToListAsync(ct);

            var isSuperAdmin = permissions.Contains("superadmin", StringComparer.OrdinalIgnoreCase);
            var isRootUser = permissions.Contains("rootuser", StringComparer.OrdinalIgnoreCase);

            var gameWrites = permissions
                .Where(p => p.StartsWith("game_write:", StringComparison.OrdinalIgnoreCase))
                .Select(p => p["game_write:".Length..])
                .Select(p => Enum.TryParse<Game>(p, true, out var g) ? g : Game.Unsupported)
                .Where(g => g != Game.Unsupported)
                .Distinct()
                .ToArray();

            // Only include users who have superadmin or game write permissions
            if (isSuperAdmin || gameWrites.Length > 0)
            {
                result.Add(new DashboardUserSummaryDto
                {
                    DiscordUserId = discordId.ToString(),
                    IsSuperAdmin = isSuperAdmin,
                    IsRootUser = isRootUser,
                    GameWritePermissions = gameWrites
                });
            }
        }

        return result
            .OrderBy(u => u.DiscordUserId)
            .ToList();
    }

    public async Task<DashboardUserSummaryDto?> GetDashboardUserByDiscordIdAsync(long discordUserId, CancellationToken ct = default)
    {
        m_Logger.LogInformation("Fetching dashboard user summary for DiscordId {DiscordUserId}.", discordUserId);

        var permissions = await m_Db.DashboardPermissions
            .Where(p => p.DiscordId == discordUserId)
            .Select(p => p.Permission)
            .ToListAsync(ct);

        if (permissions.Count == 0)
            return null;

        var isSuperAdmin = permissions.Contains("superadmin", StringComparer.OrdinalIgnoreCase);
        var isRootUser = permissions.Contains("rootuser", StringComparer.OrdinalIgnoreCase);

        var gameWrites = permissions
            .Where(p => p.StartsWith("game_write:", StringComparison.OrdinalIgnoreCase))
            .Select(p => p["game_write:".Length..])
            .Select(p => Enum.TryParse<Game>(p, true, out var g) ? g : Game.Unsupported)
            .Where(g => g != Game.Unsupported)
            .Distinct()
            .ToArray();

        return new DashboardUserSummaryDto
        {
            DiscordUserId = discordUserId.ToString(),
            IsSuperAdmin = isSuperAdmin,
            IsRootUser = isRootUser,
            GameWritePermissions = gameWrites
        };
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

        // Check if user already has permissions
        var existing = await m_Db.DashboardPermissions
            .AnyAsync(p => p.DiscordId == request.DiscordUserId, ct);

        if (existing)
        {
            return new AddDashboardUserResultDto
            {
                Succeeded = false,
                Error = "Discord user id already has permissions."
            };
        }

        var permissions = new List<DashboardPermission>();

        // Add game write permissions
        foreach (var game in (request.GameWritePermissions ?? []))
        {
            permissions.Add(new DashboardPermission
            {
                DiscordId = request.DiscordUserId,
                Permission = $"game_write:{game.ToString().ToLowerInvariant()}"
            });
        }

        m_Db.DashboardPermissions.AddRange(permissions);

        try
        {
            await m_Db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e)
        {
            m_Logger.LogError(e, "Failed to create dashboard permissions due to a database error.");
            return new AddDashboardUserResultDto
            {
                Succeeded = false,
                Error = "Failed to create user due to a database error."
            };
        }

        m_Logger.LogInformation("Dashboard permissions created for DiscordId {DiscordUserId}.", request.DiscordUserId);

        return new AddDashboardUserResultDto
        {
            Succeeded = true,
            DiscordUserId = request.DiscordUserId.ToString(),
            GameWritePermissions = [.. request.GameWritePermissions ?? []]
        };
    }

    public async Task<UpdateDashboardUserResultDto> UpdateDashboardUserByDiscordIdAsync(UpdateDashboardUserRequestDto request, CancellationToken ct = default)
    {
        m_Logger.LogInformation("Updating dashboard permissions for DiscordId {DiscordUserId}.", request.DiscordUserId);

        // Check if root user
        if (await IsRootUserAsync(request.DiscordUserId, ct))
        {
            return new UpdateDashboardUserResultDto
            {
                Succeeded = false,
                Error = "Root user cannot be modified."
            };
        }

        var existingPermissions = await m_Db.DashboardPermissions
            .Where(p => p.DiscordId == request.DiscordUserId)
            .ToListAsync(ct);

        var existingPermissionStrings = existingPermissions
            .Select(p => p.Permission)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Build target permission set
        var targetPermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (request.IsSuperAdmin)
            targetPermissions.Add("superadmin");

        foreach (var game in (request.GameWritePermissions ?? []))
            targetPermissions.Add($"game_write:{game.ToString().ToLowerInvariant()}");

        // Remove permissions that are no longer needed
        var toRemove = existingPermissions
            .Where(p => !targetPermissions.Contains(p.Permission))
            .ToList();

        // Add permissions that are new
        var toAdd = targetPermissions
            .Where(p => !existingPermissionStrings.Contains(p))
            .Select(p => new DashboardPermission
            {
                DiscordId = request.DiscordUserId,
                Permission = p
            })
            .ToList();

        m_Db.DashboardPermissions.RemoveRange(toRemove);
        m_Db.DashboardPermissions.AddRange(toAdd);

        try
        {
            await m_Db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e)
        {
            m_Logger.LogError(e, "Failed to update dashboard permissions due to a database error.");
            return new UpdateDashboardUserResultDto
            {
                Succeeded = false,
                Error = "Failed to update user due to a database error."
            };
        }

        m_Logger.LogInformation("Dashboard permissions updated for DiscordId {DiscordUserId}.", request.DiscordUserId);

        await m_SessionService.InvalidateAllForUserAsync(request.DiscordUserId, ct);

        var isRootUser = await IsRootUserAsync(request.DiscordUserId, ct);

        return new UpdateDashboardUserResultDto
        {
            Succeeded = true,
            DiscordUserId = request.DiscordUserId.ToString(),
            IsSuperAdmin = request.IsSuperAdmin,
            IsRootUser = isRootUser,
            GameWritePermissions = [.. request.GameWritePermissions ?? []]
        };
    }

    public async Task<RemoveDashboardUserResultDto> RemoveDashboardUserByDiscordIdAsync(long discordUserId, CancellationToken ct = default)
    {
        m_Logger.LogInformation("Deleting dashboard permissions for DiscordId {DiscordUserId}.", discordUserId);

        // Check if root user
        if (await IsRootUserAsync(discordUserId, ct))
        {
            return new RemoveDashboardUserResultDto
            {
                Succeeded = false,
                Error = "Root user cannot be deleted."
            };
        }

        var permissions = await m_Db.DashboardPermissions
            .Where(p => p.DiscordId == discordUserId)
            .ToListAsync(ct);

        if (permissions.Count == 0)
        {
            return new RemoveDashboardUserResultDto
            {
                Succeeded = false,
                Error = "User not found."
            };
        }

        m_Db.DashboardPermissions.RemoveRange(permissions);

        try
        {
            await m_Db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e)
        {
            m_Logger.LogError(e, "Failed to delete dashboard permissions due to a database error.");
            return new RemoveDashboardUserResultDto
            {
                Succeeded = false,
                Error = "Failed to delete user due to a database error."
            };
        }

        m_Logger.LogInformation("Dashboard permissions deleted for DiscordId {DiscordUserId}.", discordUserId);

        await m_SessionService.InvalidateAllForUserAsync(discordUserId, ct);

        return new RemoveDashboardUserResultDto
        {
            Succeeded = true
        };
    }

    public async Task<bool> IsRootUserAsync(long discordUserId, CancellationToken ct = default)
    {
        return await m_Db.DashboardPermissions
            .AnyAsync(p => p.DiscordId == discordUserId && p.Permission == "rootuser", ct);
    }
}
