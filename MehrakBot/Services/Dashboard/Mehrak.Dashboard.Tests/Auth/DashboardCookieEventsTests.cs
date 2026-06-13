using System.Net;
using System.Security.Claims;
using Mehrak.Dashboard.Shared.Auth;
using Mehrak.Domain.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;

namespace Mehrak.Dashboard.Tests.Auth;

[TestFixture]
public class DashboardCookieEventsTests
{
    private Mock<IDashboardSessionService> m_MockSessionService = null!;
    private Mock<IHttpClientFactory> m_MockHttpClientFactory = null!;
    private Mock<ILogger<DashboardCookieEvents>> m_MockLogger = null!;
    private DashboardCookieEvents m_Events = null!;

    [SetUp]
    public void SetUp()
    {
        m_MockSessionService = new Mock<IDashboardSessionService>();
        m_MockHttpClientFactory = new Mock<IHttpClientFactory>();
        m_MockLogger = new Mock<ILogger<DashboardCookieEvents>>();
        m_Events = new DashboardCookieEvents(
            m_MockSessionService.Object,
            m_MockHttpClientFactory.Object,
            m_MockLogger.Object);
    }

    private static ClaimsPrincipal CreatePrincipal(string? sessionToken = null)
    {
        var claims = new List<Claim>();
        if (sessionToken != null)
            claims.Add(new Claim("dashboard_session", sessionToken));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    private static CookieValidatePrincipalContext CreateContext(ClaimsPrincipal principal)
    {
        var httpContext = new DefaultHttpContext { User = principal };
        var scheme = new AuthenticationScheme(
            CookieAuthenticationDefaults.AuthenticationScheme,
            "Test",
            typeof(CookieAuthenticationHandler));
        var ticket = new AuthenticationTicket(principal, CookieAuthenticationDefaults.AuthenticationScheme);
        return new CookieValidatePrincipalContext(httpContext, scheme, new CookieAuthenticationOptions(), ticket);
    }

    private void SetupHttpContextWithSignOut(HttpContext httpContext)
    {
        var mockAuthService = new Mock<IAuthenticationService>();
        mockAuthService
            .Setup(a => a.SignOutAsync(
                It.IsAny<HttpContext>(),
                It.IsAny<string>(),
                It.IsAny<AuthenticationProperties>()))
            .Returns(Task.CompletedTask);

        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider.Setup(sp => sp.GetService(typeof(IAuthenticationService))).Returns(mockAuthService.Object);
        httpContext.RequestServices = mockServiceProvider.Object;
    }

    private void SetupDiscordClient(HttpStatusCode statusCode)
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(statusCode));

        var httpClient = new HttpClient(mockHandler.Object);
        m_MockHttpClientFactory.Setup(f => f.CreateClient("Default")).Returns(httpClient);
    }

    #region ValidatePrincipal

    [Test]
    public async Task ValidatePrincipal_MissingSessionClaim_RejectsAndSignsOut()
    {
        var principal = CreatePrincipal(null);
        var context = CreateContext(principal);
        SetupHttpContextWithSignOut(context.HttpContext);

        await m_Events.ValidatePrincipal(context);

        Assert.That(context.Principal, Is.Null);
    }

    [Test]
    public async Task ValidatePrincipal_SessionNotFound_RejectsAndSignsOut()
    {
        var principal = CreatePrincipal("tok123");
        var context = CreateContext(principal);
        SetupHttpContextWithSignOut(context.HttpContext);
        m_MockSessionService.Setup(s => s.GetAndRefreshSessionAsync("tok123", It.IsAny<CancellationToken>()))
            .ReturnsAsync((DashboardSessionData?)null);

        await m_Events.ValidatePrincipal(context);

        Assert.That(context.Principal, Is.Null);
    }

    [Test]
    public async Task ValidatePrincipal_ValidSession_Proceeds()
    {
        var principal = CreatePrincipal("tok123");
        var context = CreateContext(principal);
        m_MockSessionService.Setup(s => s.GetAndRefreshSessionAsync("tok123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardSessionData(100L, null, DateTime.UtcNow, null, null, null));
        m_MockSessionService.Setup(s => s.TryClaimTokenValidationAsync("tok123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await m_Events.ValidatePrincipal(context);

        Assert.That(context.Principal, Is.Not.Null);
    }

    [Test]
    public async Task ValidatePrincipal_AlreadyValidatedToday_SkipsDiscordValidation()
    {
        var principal = CreatePrincipal("tok123");
        var context = CreateContext(principal);
        m_MockSessionService.Setup(s => s.GetAndRefreshSessionAsync("tok123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardSessionData(100L, "access-token", DateTime.UtcNow, null, null, null));
        m_MockSessionService.Setup(s => s.TryClaimTokenValidationAsync("tok123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await m_Events.ValidatePrincipal(context);

        m_MockHttpClientFactory.Verify(f => f.CreateClient(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task ValidatePrincipal_DiscordReturnsUnauthorized_InvalidatesAndRejects()
    {
        var principal = CreatePrincipal("tok123");
        var context = CreateContext(principal);
        SetupHttpContextWithSignOut(context.HttpContext);
        m_MockSessionService.Setup(s => s.GetAndRefreshSessionAsync("tok123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardSessionData(100L, "access-token", DateTime.UtcNow, null, null, null));
        m_MockSessionService.Setup(s => s.TryClaimTokenValidationAsync("tok123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        SetupDiscordClient(HttpStatusCode.Unauthorized);

        await m_Events.ValidatePrincipal(context);

        Assert.Multiple(() =>
        {
            Assert.That(context.Principal, Is.Null);
            m_MockSessionService.Verify(s => s.InvalidateSessionAsync("tok123", It.IsAny<CancellationToken>()), Times.Once);
        });
    }

    [Test]
    public async Task ValidatePrincipal_DiscordReturnsForbidden_InvalidatesAndRejects()
    {
        var principal = CreatePrincipal("tok123");
        var context = CreateContext(principal);
        SetupHttpContextWithSignOut(context.HttpContext);
        m_MockSessionService.Setup(s => s.GetAndRefreshSessionAsync("tok123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardSessionData(100L, "access-token", DateTime.UtcNow, null, null, null));
        m_MockSessionService.Setup(s => s.TryClaimTokenValidationAsync("tok123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        SetupDiscordClient(HttpStatusCode.Forbidden);

        await m_Events.ValidatePrincipal(context);

        Assert.Multiple(() =>
        {
            Assert.That(context.Principal, Is.Null);
            m_MockSessionService.Verify(s => s.InvalidateSessionAsync("tok123", It.IsAny<CancellationToken>()), Times.Once);
        });
    }

    [Test]
    public async Task ValidatePrincipal_DiscordReturnsRateLimit_DoesNotReject()
    {
        var principal = CreatePrincipal("tok123");
        var context = CreateContext(principal);
        m_MockSessionService.Setup(s => s.GetAndRefreshSessionAsync("tok123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardSessionData(100L, "access-token", DateTime.UtcNow, null, null, null));
        m_MockSessionService.Setup(s => s.TryClaimTokenValidationAsync("tok123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        SetupDiscordClient(HttpStatusCode.TooManyRequests);

        await m_Events.ValidatePrincipal(context);

        Assert.Multiple(() =>
        {
            Assert.That(context.Principal, Is.Not.Null);
            m_MockSessionService.Verify(s => s.InvalidateSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        });
    }

    [Test]
    public async Task ValidatePrincipal_DiscordReturnsServerError_DoesNotReject()
    {
        var principal = CreatePrincipal("tok123");
        var context = CreateContext(principal);
        m_MockSessionService.Setup(s => s.GetAndRefreshSessionAsync("tok123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardSessionData(100L, "access-token", DateTime.UtcNow, null, null, null));
        m_MockSessionService.Setup(s => s.TryClaimTokenValidationAsync("tok123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        SetupDiscordClient(HttpStatusCode.InternalServerError);

        await m_Events.ValidatePrincipal(context);

        Assert.Multiple(() =>
        {
            Assert.That(context.Principal, Is.Not.Null);
            m_MockSessionService.Verify(s => s.InvalidateSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        });
    }

    [Test]
    public async Task ValidatePrincipal_NetworkException_DoesNotReject()
    {
        var principal = CreatePrincipal("tok123");
        var context = CreateContext(principal);
        m_MockSessionService.Setup(s => s.GetAndRefreshSessionAsync("tok123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardSessionData(100L, "access-token", DateTime.UtcNow, null, null, null));
        m_MockSessionService.Setup(s => s.TryClaimTokenValidationAsync("tok123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));
        var httpClient = new HttpClient(mockHandler.Object);
        m_MockHttpClientFactory.Setup(f => f.CreateClient("Default")).Returns(httpClient);

        await m_Events.ValidatePrincipal(context);

        Assert.Multiple(() =>
        {
            Assert.That(context.Principal, Is.Not.Null);
            m_MockSessionService.Verify(s => s.InvalidateSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        });
    }

    [Test]
    public async Task ValidatePrincipal_NullAccessToken_SkipsDiscordValidation()
    {
        var principal = CreatePrincipal("tok123");
        var context = CreateContext(principal);
        m_MockSessionService.Setup(s => s.GetAndRefreshSessionAsync("tok123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardSessionData(100L, null, DateTime.UtcNow, null, null, null));
        m_MockSessionService.Setup(s => s.TryClaimTokenValidationAsync("tok123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await m_Events.ValidatePrincipal(context);

        Assert.Multiple(() =>
        {
            Assert.That(context.Principal, Is.Not.Null);
            m_MockHttpClientFactory.Verify(f => f.CreateClient(It.IsAny<string>()), Times.Never);
        });
    }

    #endregion

    #region SigningOut

    [Test]
    public async Task SigningOut_WithSessionToken_InvalidatesSession()
    {
        var principal = CreatePrincipal("tok123");
        var httpContext = new DefaultHttpContext { User = principal };
        var mockAuthService = new Mock<IAuthenticationService>();
        mockAuthService
            .Setup(a => a.SignOutAsync(
                It.IsAny<HttpContext>(),
                It.IsAny<string>(),
                It.IsAny<AuthenticationProperties>()))
            .Returns(Task.CompletedTask);
        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider.Setup(sp => sp.GetService(typeof(IAuthenticationService))).Returns(mockAuthService.Object);
        httpContext.RequestServices = mockServiceProvider.Object;

        var scheme = new AuthenticationScheme(
            CookieAuthenticationDefaults.AuthenticationScheme,
            "Test",
            typeof(CookieAuthenticationHandler));
        var context = new CookieSigningOutContext(httpContext, scheme, new CookieAuthenticationOptions(), new AuthenticationProperties(), new CookieOptions());

        await m_Events.SigningOut(context);

        m_MockSessionService.Verify(s => s.InvalidateSessionAsync("tok123", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task SigningOut_NoSessionToken_DoesNothing()
    {
        var principal = CreatePrincipal(null);
        var httpContext = new DefaultHttpContext { User = principal };
        var scheme = new AuthenticationScheme(
            CookieAuthenticationDefaults.AuthenticationScheme,
            "Test",
            typeof(CookieAuthenticationHandler));
        var context = new CookieSigningOutContext(httpContext, scheme, new CookieAuthenticationOptions(), new AuthenticationProperties(), new CookieOptions());

        await m_Events.SigningOut(context);

        m_MockSessionService.Verify(s => s.InvalidateSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion
}
