using System.Security.Claims;
using Mehrak.Domain.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Mehrak.Dashboard.Shared.Auth;

public class DashboardCookieEvents : CookieAuthenticationEvents
{
    private const string SessionTokenClaim = "dashboard_session";
    private const string DiscordIdClaim = "discord_id";
    private const string PermissionClaim = "perm";
    private readonly IDashboardSessionService m_SessionService;
    private readonly IDashboardUserService m_UserService;
    private readonly IDashboardProfileAuthenticationService m_ProfileAuthService;
    private readonly ILogger<DashboardCookieEvents> m_Logger;

    public DashboardCookieEvents(
        IDashboardSessionService sessionService,
        IDashboardUserService userService,
        IDashboardProfileAuthenticationService profileAuthService,
        ILogger<DashboardCookieEvents> logger)
    {
        m_SessionService = sessionService;
        m_UserService = userService;
        m_ProfileAuthService = profileAuthService;
        m_Logger = logger;
    }

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var sessionToken = context.Principal?.Claims.FirstOrDefault(c => c.Type == SessionTokenClaim)?.Value;
        if (string.IsNullOrWhiteSpace(sessionToken))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync();
            return;
        }

        // Finding 10: session validation on the hot path is read-only. A
        // valid session is never rewritten per request, so over-limit
        // requests rejected by the IP rate limiter perform no session writes.
        var session = await m_SessionService.GetSessionAsync(sessionToken, context.HttpContext.RequestAborted);
        if (session == null)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync();
            return;
        }

        // Rebuild the principal from current server-side state on every request so a
        // login that raced permission revocation (or any stale cookie) cannot retain
        // removed privileges for the cookie lifetime. The session row is authoritative
        // for identity; the permissions table is authoritative for authorization.
        var summary = await m_UserService.GetDashboardUserByDiscordIdAsync(
            session.DiscordUserId, context.HttpContext.RequestAborted);

        var claims = new List<Claim>
        {
            new(DiscordIdClaim, session.DiscordUserId.ToString()),
            new(SessionTokenClaim, sessionToken)
        };

        if (summary is not null)
        {
            if (summary.IsSuperAdmin)
                claims.Add(new Claim(ClaimTypes.Role, "superadmin"));

            if (summary.IsRootUser)
                claims.Add(new Claim(ClaimTypes.Role, "rootuser"));

            foreach (var game in summary.GameWritePermissions)
                claims.Add(new Claim(PermissionClaim, $"game_write:{game.ToString().ToLowerInvariant()}"));
        }

        context.ReplacePrincipal(new ClaimsPrincipal(
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
        context.ShouldRenew = true;
    }

    public override async Task SigningOut(CookieSigningOutContext context)
    {
        var sessionToken = context.HttpContext.User?.Claims.FirstOrDefault(c => c.Type == SessionTokenClaim)?.Value;
        if (!string.IsNullOrWhiteSpace(sessionToken))
        {
            await m_SessionService.InvalidateSessionAsync(sessionToken, context.HttpContext.RequestAborted);
        }

        // Finding 8: logout clears every profile unlock for the user (both
        // Bot and Dashboard caches) so a later session cannot reuse them.
        // Best-effort: logout itself must never fail because of this.
        try
        {
            var discordId = ParseDiscordId(context.HttpContext.User)
                ?? await GetSessionDiscordIdAsync(sessionToken, context.HttpContext.RequestAborted);
            if (discordId.HasValue)
                await m_ProfileAuthService.RevokeAllAsync(discordId.Value, context.HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            m_Logger.LogWarning(ex, "Failed to revoke profile unlocks at logout");
        }
    }

    private static ulong? ParseDiscordId(ClaimsPrincipal? principal)
    {
        var claimValue = principal?.Claims.FirstOrDefault(c => c.Type == DiscordIdClaim)?.Value;
        return ulong.TryParse(claimValue, out var discordId) ? discordId : null;
    }

    private async Task<ulong?> GetSessionDiscordIdAsync(string? sessionToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sessionToken))
            return null;

        try
        {
            var session = await m_SessionService.GetSessionAsync(sessionToken, ct);
            return session is null ? null : (ulong)session.DiscordUserId;
        }
        catch (Exception ex)
        {
            m_Logger.LogDebug(ex, "Failed to resolve session identity at logout");
            return null;
        }
    }
}
