using Mehrak.Domain.Auth;
using Mehrak.Domain.Auth.Dtos;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Infrastructure.Auth.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Mehrak.Infrastructure.Auth.Services;

public class DashboardAuthService : IDashboardAuthService
{
    private readonly DashboardAuthDbContext m_Db;
    private readonly IDashboardSessionService m_SessionService;
    private readonly ILogger<DashboardAuthService> m_Logger;

    public DashboardAuthService(DashboardAuthDbContext db, IDashboardSessionService sessionService, ILogger<DashboardAuthService> logger)
    {
        m_Db = db;
        m_SessionService = sessionService;
        m_Logger = logger;
    }

    public async Task<LoginResultDto> LoginByDiscordAsync(long discordId, string discordUsername, string? avatarHash, string? accessToken, string? loginIp, string? userAgent, string? location, CancellationToken ct = default)
    {
        m_Logger.LogInformation("Discord login attempt for DiscordId {DiscordId}", discordId);

        // Load permissions for this user
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

        // Invalidate any existing sessions for this user (single-session enforcement)
        await m_SessionService.InvalidateAllForUserAsync(discordId, ct);

        // Create new session
        var sessionToken = Guid.NewGuid().ToString("N");
        await m_SessionService.CreateSessionAsync(sessionToken, discordId, accessToken, loginIp, userAgent, location, ct);

        m_Logger.LogInformation("Discord login succeeded for DiscordId {DiscordId}", discordId);

        return new LoginResultDto
        {
            Succeeded = true,
            DiscordUserId = discordId,
            SessionToken = sessionToken,
            IsSuperAdmin = isSuperAdmin,
            IsRootUser = isRootUser,
            GameWritePermissions = gameWrites
        };
    }

    public async Task InvalidateSessionAsync(string sessionToken, CancellationToken ct = default)
    {
        await m_SessionService.InvalidateSessionAsync(sessionToken, ct);
    }

    public async Task<string?> GetAccessTokenAsync(string sessionToken, CancellationToken ct = default)
    {
        var session = await m_SessionService.GetSessionAsync(sessionToken, ct);
        return session?.AccessToken;
    }
}
