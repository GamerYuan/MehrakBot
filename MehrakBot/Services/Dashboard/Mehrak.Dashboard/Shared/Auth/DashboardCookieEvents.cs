using System.Net.Http.Json;
using Mehrak.Domain.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Memory;

namespace Mehrak.Dashboard.Shared.Auth;

public class DashboardCookieEvents : CookieAuthenticationEvents
{
    private const string SessionTokenClaim = "dashboard_session";
    private static readonly TimeSpan TokenValidationCacheDuration = TimeSpan.FromMinutes(5);
    private readonly IDashboardAuthService m_AuthService;
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly IMemoryCache m_Cache;
    private readonly ILogger<DashboardCookieEvents> m_Logger;

    public DashboardCookieEvents(
        IDashboardAuthService authService,
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        ILogger<DashboardCookieEvents> logger)
    {
        m_AuthService = authService;
        m_HttpClientFactory = httpClientFactory;
        m_Cache = cache;
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

        var valid = await m_AuthService.ValidateSessionAsync(sessionToken, context.HttpContext.RequestAborted);
        if (!valid)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync();
            return;
        }

        var cacheKey = $"discord_token_valid:{sessionToken}";
        if (m_Cache.TryGetValue(cacheKey, out _))
            return;

        var accessToken = await m_AuthService.GetAccessTokenAsync(sessionToken, context.HttpContext.RequestAborted);
        if (string.IsNullOrWhiteSpace(accessToken))
            return;

        try
        {
            var httpClient = m_HttpClientFactory.CreateClient("Default");
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            var response = await httpClient.GetAsync("https://discord.com/api/v10/oauth2/@me", context.HttpContext.RequestAborted);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogWarning("Discord token validation failed for session {Token} with status {Status}",
                    sessionToken[..6], response.StatusCode);
                await m_AuthService.InvalidateSessionAsync(sessionToken, context.HttpContext.RequestAborted);
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync();
                return;
            }

            m_Cache.Set(cacheKey, true, TokenValidationCacheDuration);
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
            m_Cache.Remove($"discord_token_valid:{sessionToken}");
            await m_AuthService.InvalidateSessionAsync(sessionToken, context.HttpContext.RequestAborted);
        }
    }
}
