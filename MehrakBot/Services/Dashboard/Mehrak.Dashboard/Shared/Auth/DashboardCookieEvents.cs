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

        var session = await m_SessionService.GetAndRefreshSessionAsync(sessionToken, context.HttpContext.RequestAborted);
        if (session == null)
        {
            m_Logger.LogWarning("Session not found for token {Token}", sessionToken[..Math.Min(6, sessionToken.Length)]);
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync();
            return;
        }

        // Atomically check if daily token validation is needed and claim it
        if (!await m_SessionService.TryClaimTokenValidationAsync(sessionToken, context.HttpContext.RequestAborted))
            return;

        if (string.IsNullOrWhiteSpace(session.AccessToken))
            return;

        try
        {
            var httpClient = m_HttpClientFactory.CreateClient("Default");
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session.AccessToken);
            var response = await httpClient.GetAsync("https://discord.com/api/v10/oauth2/@me", context.HttpContext.RequestAborted);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogWarning("Discord token validation failed for session {Token} with status {Status}",
                    sessionToken[..Math.Min(6, sessionToken.Length)], response.StatusCode);

                // Only revoke the session when Discord indicates the token is invalid/expired.
                // Transient failures (429, 5xx) or network errors should not force a mass logout.
                if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                {
                    await m_SessionService.InvalidateSessionAsync(sessionToken, context.HttpContext.RequestAborted);
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync();
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Failed to validate Discord token for session {Token}", sessionToken[..Math.Min(6, sessionToken.Length)]);
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
