using Mehrak.Domain.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Mehrak.Dashboard.Shared.Auth;

public class DashboardCookieEvents : CookieAuthenticationEvents
{
    private const string SessionTokenClaim = "dashboard_session";
    private readonly IDashboardSessionService m_SessionService;

    public DashboardCookieEvents(IDashboardSessionService sessionService)
    {
        m_SessionService = sessionService;
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
