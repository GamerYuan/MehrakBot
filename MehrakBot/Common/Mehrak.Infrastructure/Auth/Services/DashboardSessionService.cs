using Mehrak.Domain.Auth;
using Mehrak.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Mehrak.Infrastructure.Auth.Services;

public class DashboardSessionService : IDashboardSessionService
{
    private static readonly TimeSpan SessionTtl = TimeSpan.FromDays(7);
    private static readonly TimeSpan CleanupThreshold = TimeSpan.FromHours(1);

    private readonly DashboardAuthDbContext m_Db;
    private readonly ILogger<DashboardSessionService> m_Logger;

    public DashboardSessionService(DashboardAuthDbContext db, ILogger<DashboardSessionService> logger)
    {
        m_Db = db;
        m_Logger = logger;
    }

    public async Task CreateSessionAsync(string sessionToken, long discordUserId, string? accessToken, string? loginIp, string? userAgent, string? location, CancellationToken ct = default)
    {
        var session = new Entities.DashboardSession
        {
            Token = sessionToken,
            DiscordId = discordUserId,
            AccessToken = accessToken,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow + SessionTtl,
            LoginIp = loginIp,
            UserAgent = userAgent,
            Location = location
        };

        m_Db.DashboardSessions.Add(session);
        await m_Db.SaveChangesAsync(ct);

        m_Logger.LogInformation("Session created for DiscordId {DiscordUserId}", discordUserId);
    }

    public async Task<DashboardSessionData?> GetSessionAsync(string sessionToken, CancellationToken ct = default)
    {
        var session = await m_Db.DashboardSessions
            .FirstOrDefaultAsync(s => s.Token == sessionToken, ct);

        if (session == null || session.ExpiresAt <= DateTime.UtcNow)
            return null;

        return new DashboardSessionData(
            session.DiscordId,
            session.AccessToken,
            session.LastTokenValidation ?? session.CreatedAt,
            session.LoginIp,
            session.UserAgent,
            session.Location);
    }

    public async Task<DashboardSessionData?> GetAndRefreshSessionAsync(string sessionToken, CancellationToken ct = default)
    {
        var session = await m_Db.DashboardSessions
            .FirstOrDefaultAsync(s => s.Token == sessionToken, ct);

        if (session == null || session.ExpiresAt <= DateTime.UtcNow)
            return null;

        session.ExpiresAt = DateTime.UtcNow + SessionTtl;
        await m_Db.SaveChangesAsync(ct);

        return new DashboardSessionData(
            session.DiscordId,
            session.AccessToken,
            session.LastTokenValidation ?? session.CreatedAt,
            session.LoginIp,
            session.UserAgent,
            session.Location);
    }

    public async Task RefreshSessionAsync(string sessionToken, CancellationToken ct = default)
    {
        var session = await m_Db.DashboardSessions
            .FirstOrDefaultAsync(s => s.Token == sessionToken, ct);

        if (session == null || session.ExpiresAt <= DateTime.UtcNow)
            return;

        session.ExpiresAt = DateTime.UtcNow + SessionTtl;
        await m_Db.SaveChangesAsync(ct);
    }

    public async Task InvalidateSessionAsync(string sessionToken, CancellationToken ct = default)
    {
        var session = await m_Db.DashboardSessions
            .FirstOrDefaultAsync(s => s.Token == sessionToken, ct);

        if (session != null)
        {
            m_Db.DashboardSessions.Remove(session);
            await m_Db.SaveChangesAsync(ct);
            m_Logger.LogInformation("Session invalidated: {Token}", sessionToken[..Math.Min(6, sessionToken.Length)]);
        }
    }

    public async Task InvalidateAllForUserAsync(long discordUserId, CancellationToken ct = default)
    {
        var sessions = await m_Db.DashboardSessions
            .Where(s => s.DiscordId == discordUserId)
            .ToListAsync(ct);

        m_Db.DashboardSessions.RemoveRange(sessions);
        await m_Db.SaveChangesAsync(ct);

        m_Logger.LogInformation("All sessions invalidated for DiscordId {DiscordUserId}", discordUserId);
    }

    public async Task<bool> TryClaimTokenValidationAsync(string sessionToken, CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.Date;
        var now = DateTime.UtcNow;

        var rowsUpdated = await m_Db.DashboardSessions
            .Where(s => s.Token == sessionToken
                         && s.ExpiresAt > DateTime.UtcNow
                         && (s.LastTokenValidation == null || s.LastTokenValidation < today))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.LastTokenValidation, now), ct);

        return rowsUpdated > 0;
    }

    public async Task<int> CleanupExpiredSessionsAsync(CancellationToken ct = default)
    {
        var threshold = DateTime.UtcNow - CleanupThreshold;
        var count = await m_Db.DashboardSessions
            .Where(s => s.ExpiresAt < threshold)
            .ExecuteDeleteAsync(ct);

        if (count > 0)
            m_Logger.LogInformation("Cleaned up {Count} expired sessions", count);

        return count;
    }
}
