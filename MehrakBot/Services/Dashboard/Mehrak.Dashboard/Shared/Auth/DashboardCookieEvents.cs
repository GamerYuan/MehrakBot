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

    public DashboardCookieEvents(IDashboardSessionService sessionService, IDashboardUserService userService)
    {
        m_SessionService = sessionService;
        m_UserService = userService;
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

        var session = await m_SessionService.GetAndRefreshSessionAsync(sessionToken, context.HttpContext.RequestAborted);
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
    }
}
