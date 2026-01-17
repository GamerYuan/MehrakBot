using System.Security.Claims;
using Mehrak.Domain.Auth;
using Mehrak.Domain.Services.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Mehrak.Dashboard.Auth;

public class DashboardCookieEvents : CookieAuthenticationEvents
{
    private const string SessionTokenClaim = "dashboard_session";
    private readonly IDashboardAuthService m_AuthService;
    private readonly IDashboardMetrics m_Metrics;

    public DashboardCookieEvents(IDashboardAuthService authService, IDashboardMetrics metrics)
    {
        m_AuthService = authService;
        m_Metrics = metrics;
    }

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var sessionToken = context.Principal?.Claims.FirstOrDefault(c => c.Type == SessionTokenClaim)?.Value;
        if (string.IsNullOrWhiteSpace(sessionToken))
        {
            TrackLogout(context);
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync();
            return;
        }

        var valid = await m_AuthService.ValidateSessionAsync(sessionToken, context.HttpContext.RequestAborted);
        if (!valid)
        {
            TrackLogout(context);
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync();
        }
    }

    public override async Task SigningOut(CookieSigningOutContext context)
    {
        var sessionToken = context.HttpContext.User?.Claims.FirstOrDefault(c => c.Type == SessionTokenClaim)?.Value;
        if (!string.IsNullOrWhiteSpace(sessionToken))
        {
            await m_AuthService.InvalidateSessionAsync(sessionToken, context.HttpContext.RequestAborted);
        }
    }

    private void TrackLogout(CookieValidatePrincipalContext context)
    {
        var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrWhiteSpace(userId))
        {
            m_Metrics.TrackUserLogout(userId);
        }
    }
}
