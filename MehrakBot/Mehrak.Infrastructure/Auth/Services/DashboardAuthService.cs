using System.Linq;
using Mehrak.Domain.Auth;
using Mehrak.Domain.Auth.Dtos;
using Mehrak.Infrastructure.Auth.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Mehrak.Infrastructure.Auth.Services;

public class DashboardAuthService : IDashboardAuthService
{
    private readonly DashboardAuthDbContext m_Db;
    private readonly PasswordHasher<DashboardUser> m_Hasher = new();

    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(1);

    public DashboardAuthService(DashboardAuthDbContext db)
    {
        m_Db = db;
    }

    public async Task<LoginResultDto> LoginAsync(LoginRequestDto request, CancellationToken ct = default)
    {
        var user = await m_Db.DashboardUsers
            .Include(u => u.Sessions)
            .Include(u => u.GamePermissions)
            .SingleOrDefaultAsync(u => u.Username == request.Username && u.IsActive, ct);

        if (user == null)
            return new LoginResultDto { Succeeded = false, Error = "Invalid credentials." };

        var verify = m_Hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verify == PasswordVerificationResult.Failed)
            return new LoginResultDto { Succeeded = false, Error = "Invalid credentials." };

        // Uniqueness: remove previous sessions
        m_Db.DashboardSessions.RemoveRange(user.Sessions);

        var sessionToken = Guid.NewGuid().ToString("N");
        var session = new DashboardSession
        {
            UserId = user.Id,
            SessionToken = sessionToken,
            ExpiresAtUtc = DateTime.UtcNow.Add(SessionLifetime)
        };

        user.UpdatedAtUtc = DateTime.UtcNow;
        m_Db.DashboardSessions.Add(session);
        await m_Db.SaveChangesAsync(ct);

        var gameWrites = user.GamePermissions
            .Where(p => p.AllowWrite)
            .Select(p => p.GameCode)
            .Distinct()
            .ToArray();

        return new LoginResultDto
        {
            Succeeded = true,
            UserId = user.Id,
            Username = user.Username,
            DiscordUserId = user.DiscordId,
            SessionToken = sessionToken,
            IsSuperAdmin = user.IsSuperAdmin,
            GameWritePermissions = gameWrites,
            RequiresPasswordReset = user.RequirePasswordReset
        };
    }

    public async Task<bool> ValidateSessionAsync(string sessionToken, CancellationToken ct = default)
    {
        var session = await m_Db.DashboardSessions
            .Include(s => s.User)
            .SingleOrDefaultAsync(s => s.SessionToken == sessionToken, ct);

        if (session == null || session.IsExpired() || !session.User.IsActive)
            return false;

        return true;
    }

    public async Task InvalidateSessionAsync(string sessionToken, CancellationToken ct = default)
    {
        var session = await m_Db.DashboardSessions.SingleOrDefaultAsync(s => s.SessionToken == sessionToken, ct);
        if (session != null)
        {
            m_Db.DashboardSessions.Remove(session);
            await m_Db.SaveChangesAsync(ct);
        }
    }

    public async Task<ChangeDashboardPasswordResultDto> ChangePasswordAsync(ChangeDashboardPasswordRequestDto request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
        {
            return new ChangeDashboardPasswordResultDto
            {
                Succeeded = false,
                Error = "New password is too short."
            };
        }

        var user = await m_Db.DashboardUsers
            .Include(u => u.Sessions)
            .SingleOrDefaultAsync(u => u.Id == request.UserId && u.IsActive, ct);

        if (user == null)
        {
            return new ChangeDashboardPasswordResultDto
            {
                Succeeded = false,
                Error = "User not found."
            };
        }

        var verify = m_Hasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword);
        if (verify == PasswordVerificationResult.Failed)
        {
            return new ChangeDashboardPasswordResultDto
            {
                Succeeded = false,
                Error = "Current password is incorrect."
            };
        }

        user.PasswordHash = m_Hasher.HashPassword(user, request.NewPassword);
        user.RequirePasswordReset = false;
        user.UpdatedAtUtc = DateTime.UtcNow;

        var hadSessions = user.Sessions.Count > 0;
        m_Db.DashboardSessions.RemoveRange(user.Sessions);

        await m_Db.SaveChangesAsync(ct);

        return new ChangeDashboardPasswordResultDto
        {
            Succeeded = true,
            SessionsInvalidated = hadSessions
        };
    }

    public async Task<ChangeDashboardPasswordResultDto> ForceResetPasswordAsync(ForceResetDashboardPasswordRequestDto request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
        {
            return new ChangeDashboardPasswordResultDto
            {
                Succeeded = false,
                Error = "New password is too short."
            };
        }

        var user = await m_Db.DashboardUsers
            .Include(u => u.Sessions)
            .SingleOrDefaultAsync(u => u.Id == request.UserId && u.IsActive, ct);

        if (user == null)
        {
            return new ChangeDashboardPasswordResultDto
            {
                Succeeded = false,
                Error = "User not found."
            };
        }

        if (!user.RequirePasswordReset)
        {
            return new ChangeDashboardPasswordResultDto
            {
                Succeeded = false,
                Error = "Password reset is not required."
            };
        }

        user.PasswordHash = m_Hasher.HashPassword(user, request.NewPassword);
        user.RequirePasswordReset = false;
        user.UpdatedAtUtc = DateTime.UtcNow;

        var hadSessions = user.Sessions.Count > 0;
        m_Db.DashboardSessions.RemoveRange(user.Sessions);

        await m_Db.SaveChangesAsync(ct);

        return new ChangeDashboardPasswordResultDto
        {
            Succeeded = true,
            SessionsInvalidated = hadSessions
        };
    }
}
