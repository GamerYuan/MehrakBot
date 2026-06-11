using Mehrak.Domain.Auth;
using Mehrak.Domain.Auth.Dtos;
using Mehrak.Infrastructure.Auth.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Mehrak.Infrastructure.Auth.Services;

public class DashboardAuthService : IDashboardAuthService
{
    private readonly DashboardAuthDbContext m_Db;
    private readonly ILogger<DashboardAuthService> m_Logger;

    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(1);

    public DashboardAuthService(DashboardAuthDbContext db, ILogger<DashboardAuthService> logger)
    {
        m_Db = db;
        m_Logger = logger;
    }

    public async Task<LoginResultDto> LoginByDiscordAsync(long discordId, string discordUsername, string? avatarHash, string? accessToken, CancellationToken ct = default)
    {
        m_Logger.LogInformation("Discord login attempt for DiscordId {DiscordId}", discordId);

        var user = await m_Db.DashboardUsers
            .Include(u => u.Sessions)
            .Include(u => u.GamePermissions)
            .SingleOrDefaultAsync(u => u.DiscordId == discordId && u.IsActive, ct);

        if (user == null)
        {
            m_Logger.LogWarning("Discord login failed: no active user with DiscordId {DiscordId}", discordId);
            return new LoginResultDto { Succeeded = false, Error = "No dashboard account linked to this Discord user." };
        }

        if (!string.IsNullOrWhiteSpace(discordUsername) && user.Username != discordUsername)
        {
            user.Username = discordUsername;
        }

        // Remove previous sessions for uniqueness
        m_Db.DashboardSessions.RemoveRange(user.Sessions);

        var sessionToken = Guid.NewGuid().ToString("N");
        var session = new DashboardSession
        {
            UserId = user.Id,
            SessionToken = sessionToken,
            AccessToken = accessToken,
            ExpiresAtUtc = DateTime.UtcNow.Add(SessionLifetime)
        };

        user.UpdatedAtUtc = DateTime.UtcNow;
        m_Db.DashboardSessions.Add(session);
        await m_Db.SaveChangesAsync(ct);

        m_Logger.LogInformation("Discord login succeeded for user {UserId}", user.Id);

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
            IsRootUser = user.IsRootUser,
            GameWritePermissions = gameWrites
        };
    }

    public async Task<bool> ValidateSessionAsync(string sessionToken, CancellationToken ct = default)
    {
        var session = await m_Db.DashboardSessions
            .Include(s => s.User)
            .SingleOrDefaultAsync(s => s.SessionToken == sessionToken, ct);

        if (session == null || session.IsExpired() || !session.User.IsActive)
        {
            m_Logger.LogWarning("Session validation failed for token {Token}.", sessionToken[..6]);
            return false;
        }

        return true;
    }

    public async Task InvalidateSessionAsync(string sessionToken, CancellationToken ct = default)
    {
        m_Logger.LogInformation("Invalidating session {Token}.", sessionToken[..6]);
        var session = await m_Db.DashboardSessions.SingleOrDefaultAsync(s => s.SessionToken == sessionToken, ct);
        if (session != null)
        {
            m_Db.DashboardSessions.Remove(session);
            await m_Db.SaveChangesAsync(ct);
        }
    }

    public async Task<string?> GetAccessTokenAsync(string sessionToken, CancellationToken ct = default)
    {
        var session = await m_Db.DashboardSessions
            .Include(s => s.User)
            .SingleOrDefaultAsync(s => s.SessionToken == sessionToken && s.User.IsActive, ct);

        if (session == null || session.IsExpired())
            return null;

        return session.AccessToken;
    }
}
