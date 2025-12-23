using System.Security.Cryptography;
using Mehrak.Domain.Auth;
using Mehrak.Domain.Auth.Dtos;
using Mehrak.Infrastructure.Auth.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Mehrak.Infrastructure.Auth.Services;

public class DashboardUserService : IDashboardUserService
{
    private readonly DashboardAuthDbContext m_Db;
    private readonly PasswordHasher<DashboardUser> m_Hasher = new();

    private const string TemporaryPasswordCharacters = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz0123456789!@#$%^&*_-";
    private const int TemporaryPasswordLength = 16;

    public DashboardUserService(DashboardAuthDbContext db)
    {
        m_Db = db;
    }

    public async Task<IReadOnlyCollection<DashboardUserSummaryDto>> GetDashboardUsersAsync(CancellationToken ct = default)
    {
        var users = await m_Db.DashboardUsers
            .Include(u => u.GamePermissions)
            .OrderBy(u => u.Username)
            .ToListAsync(ct);

        return [.. users
            .Select(u => new DashboardUserSummaryDto
            {
                UserId = u.Id,
                Username = u.Username,
                IsSuperAdmin = u.IsSuperAdmin,
                GameWritePermissions = [.. u.GamePermissions
                    .Where(p => p.AllowWrite)
                    .Select(p => p.GameCode)
                    .Distinct(StringComparer.OrdinalIgnoreCase)]
            })];
    }

    public async Task<AddDashboardUserResultDto> AddDashboardUserAsync(AddDashboardUserRequestDto request, CancellationToken ct = default)
    {
        var username = request.Username?.Trim();
        if (string.IsNullOrWhiteSpace(username))
        {
            return new AddDashboardUserResultDto
            {
                Succeeded = false,
                Error = "Username is required."
            };
        }

        if (request.DiscordUserId <= 0)
        {
            return new AddDashboardUserResultDto
            {
                Succeeded = false,
                Error = "Discord user id must be a positive number."
            };
        }

        if (await m_Db.DashboardUsers.AnyAsync(u => u.Username == username, ct))
        {
            return new AddDashboardUserResultDto
            {
                Succeeded = false,
                Error = "Username is already in use."
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
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim().ToLowerInvariant())
            .Distinct()
            .ToArray();

        var user = new DashboardUser
        {
            Username = username,
            DiscordId = request.DiscordUserId,
            IsSuperAdmin = request.IsSuperAdmin,
            IsActive = true,
            RequirePasswordReset = true,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var tempPassword = GenerateTemporaryPassword();
        user.PasswordHash = m_Hasher.HashPassword(user, tempPassword);
        user.GamePermissions = normalizedPermissions
            .Select(code => new DashboardGamePermission
            {
                GameCode = code,
                AllowWrite = true,
                User = user
            })
            .ToList();

        m_Db.DashboardUsers.Add(user);
        await m_Db.SaveChangesAsync(ct);

        return new AddDashboardUserResultDto
        {
            Succeeded = true,
            UserId = user.Id,
            Username = user.Username,
            TemporaryPassword = tempPassword,
            RequiresPasswordReset = user.RequirePasswordReset,
            GameWritePermissions = normalizedPermissions
        };
    }

    public async Task<UpdateDashboardUserResultDto> UpdateDashboardUserAsync(UpdateDashboardUserRequestDto request, CancellationToken ct = default)
    {
        var username = request.Username?.Trim();
        if (string.IsNullOrWhiteSpace(username))
        {
            return new UpdateDashboardUserResultDto
            {
                Succeeded = false,
                Error = "Username is required."
            };
        }

        if (request.DiscordUserId <= 0)
        {
            return new UpdateDashboardUserResultDto
            {
                Succeeded = false,
                Error = "Discord user id must be a positive number."
            };
        }

        var user = await m_Db.DashboardUsers
            .Include(u => u.GamePermissions)
            .SingleOrDefaultAsync(u => u.Id == request.UserId, ct);

        if (user == null)
        {
            return new UpdateDashboardUserResultDto
            {
                Succeeded = false,
                Error = "User not found."
            };
        }

        if (await m_Db.DashboardUsers.AnyAsync(u => u.Username == username && u.Id != user.Id, ct))
        {
            return new UpdateDashboardUserResultDto
            {
                Succeeded = false,
                Error = "Username is already in use."
            };
        }

        if (await m_Db.DashboardUsers.AnyAsync(u => u.DiscordId == request.DiscordUserId && u.Id != user.Id, ct))
        {
            return new UpdateDashboardUserResultDto
            {
                Succeeded = false,
                Error = "Discord user id is already in use."
            };
        }

        var normalizedPermissions = (request.GameWritePermissions ?? Array.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim().ToLowerInvariant())
            .Distinct()
            .ToArray();

        user.Username = username;
        user.DiscordId = request.DiscordUserId;
        user.IsSuperAdmin = request.IsSuperAdmin;
        user.IsActive = request.IsActive;
        user.UpdatedAtUtc = DateTime.UtcNow;

        var existingCodes = user.GamePermissions.ToDictionary(p => p.GameCode, StringComparer.OrdinalIgnoreCase);
        foreach (var permission in user.GamePermissions.
            Where(permission => !normalizedPermissions.Contains(permission.GameCode, StringComparer.OrdinalIgnoreCase)))
        {
            m_Db.DashboardGamePermissions.Remove(permission);
        }

        foreach (var code in normalizedPermissions
            .Where(code => !existingCodes.Keys.Any(k => string.Equals(k, code, StringComparison.OrdinalIgnoreCase))))
        {
            user.GamePermissions.Add(new DashboardGamePermission
            {
                User = user,
                GameCode = code,
                AllowWrite = true
            });
        }

        await m_Db.SaveChangesAsync(ct);

        return new UpdateDashboardUserResultDto
        {
            Succeeded = true,
            UserId = user.Id,
            Username = user.Username,
            IsActive = user.IsActive,
            IsSuperAdmin = user.IsSuperAdmin,
            GameWritePermissions = normalizedPermissions
        };
    }

    public async Task<RemoveDashboardUserResultDto> RemoveDashboardUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await m_Db.DashboardUsers.SingleOrDefaultAsync(u => u.Id == userId, ct);
        if (user == null)
        {
            return new RemoveDashboardUserResultDto
            {
                Succeeded = false,
                Error = "User not found."
            };
        }

        m_Db.DashboardUsers.Remove(user);
        await m_Db.SaveChangesAsync(ct);

        return new RemoveDashboardUserResultDto
        {
            Succeeded = true
        };
    }

    public async Task<DashboardUserRequireResetResultDto> RequirePasswordResetAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await m_Db.DashboardUsers
            .Include(u => u.Sessions)
            .SingleOrDefaultAsync(u => u.Id == userId, ct);

        if (user == null)
        {
            return new DashboardUserRequireResetResultDto
            {
                Succeeded = false,
                Error = "User not found."
            };
        }

        if (user.RequirePasswordReset)
        {
            return new DashboardUserRequireResetResultDto
            {
                Succeeded = true,
                SessionsInvalidated = false
            };
        }

        user.RequirePasswordReset = true;
        user.UpdatedAtUtc = DateTime.UtcNow;

        var hadSessions = user.Sessions.Count > 0;
        if (hadSessions)
            m_Db.DashboardSessions.RemoveRange(user.Sessions);

        await m_Db.SaveChangesAsync(ct);

        return new DashboardUserRequireResetResultDto
        {
            Succeeded = true,
            SessionsInvalidated = hadSessions
        };
    }

    private static string GenerateTemporaryPassword(int length = TemporaryPasswordLength)
    {
        var passwordChars = new char[length];
        Span<byte> buffer = stackalloc byte[length];
        RandomNumberGenerator.Fill(buffer);

        for (var i = 0; i < length; i++)
        {
            var index = buffer[i] % TemporaryPasswordCharacters.Length;
            passwordChars[i] = TemporaryPasswordCharacters[index];
        }

        return new string(passwordChars);
    }
}
