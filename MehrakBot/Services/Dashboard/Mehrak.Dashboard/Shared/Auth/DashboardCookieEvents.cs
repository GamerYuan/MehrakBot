using System.Net.Http.Json;
using Mehrak.Domain.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Mehrak.Dashboard.Shared.Auth;

public class DashboardCookieEvents : CookieAuthenticationEvents
{
    private const string SessionTokenClaim = "dashboard_session";
    private readonly IDashboardSessionService m_SessionService;
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<DashboardCookieEvents> m_Logger;

    public DashboardCookieEvents(
        IDashboardSessionService sessionService,
        IHttpClientFactory httpClientFactory,
        ILogger<DashboardCookieEvents> logger)
    {
        m_SessionService = sessionService;
        m_HttpClientFactory = httpClientFactory;
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

        var session = await m_SessionService.GetSessionAsync(sessionToken, context.HttpContext.RequestAborted);
        if (session == null)
        {
            m_Logger.LogWarning("Session not found for token {Token}", sessionToken[..6]);
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync();
            return;
        }

        // Refresh session TTL on each request (sliding window)
        await m_SessionService.RefreshSessionAsync(sessionToken, context.HttpContext.RequestAborted);

        // Check if daily token validation is needed
        if (!await m_SessionService.NeedsTokenValidationAsync(sessionToken, context.HttpContext.RequestAborted))
            return;

        if (string.IsNullOrWhiteSpace(session.AccessToken))
        {
            await m_SessionService.MarkTokenValidatedAsync(sessionToken, context.HttpContext.RequestAborted);
            return;
        }

        try
        {
            var httpClient = m_HttpClientFactory.CreateClient("Default");
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session.AccessToken);
            var response = await httpClient.GetAsync("https://discord.com/api/v10/oauth2/@me", context.HttpContext.RequestAborted);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogWarning("Discord token validation failed for session {Token} with status {Status}",
                    sessionToken[..6], response.StatusCode);

                // Only revoke the session when Discord indicates the token is invalid/expired.
                // Transient failures (429, 5xx) or network errors should not force a mass logout.
                if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                {
                    await m_SessionService.InvalidateSessionAsync(sessionToken, context.HttpContext.RequestAborted);
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync();
                    return;
                }

                // Mark validated so we don't hammer Discord while it's rate-limiting or down.
                await m_SessionService.MarkTokenValidatedAsync(sessionToken, context.HttpContext.RequestAborted);
                return;
            }

            await m_SessionService.MarkTokenValidatedAsync(sessionToken, context.HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Failed to validate Discord token for session {Token}", sessionToken[..6]);
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
