﻿﻿using System.Security.Claims;
using Mehrak.Dashboard.Shared.Auth;
using Mehrak.Domain.Auth;
using Mehrak.Domain.Auth.Dtos;
using Mehrak.Domain.Shared.Enums;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mehrak.Dashboard.Tests.Auth;

[TestFixture]
public class DashboardCookieEventsTests
{
    private Mock<IDashboardSessionService> m_MockSessionService = null!;
    private Mock<IDashboardUserService> m_MockUserService = null!;
    private Mock<IDashboardProfileAuthenticationService> m_MockProfileAuthService = null!;
    private DashboardCookieEvents m_Events = null!;

    [SetUp]
    public void SetUp()
    {
        m_MockSessionService = new Mock<IDashboardSessionService>();
        m_MockUserService = new Mock<IDashboardUserService>();
        m_MockProfileAuthService = new Mock<IDashboardProfileAuthenticationService>();
        m_Events = new DashboardCookieEvents(
            m_MockSessionService.Object,
            m_MockUserService.Object,
            m_MockProfileAuthService.Object,
            Mock.Of<ILogger<DashboardCookieEvents>>());
    }

    private static ClaimsPrincipal CreatePrincipal(string? sessionToken = null, params Claim[] extraClaims)
    {
        var claims = new List<Claim>();
        if (sessionToken != null)
            claims.Add(new Claim("dashboard_session", sessionToken));
        claims.AddRange(extraClaims);

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
        m_MockSessionService.Setup(s => s.GetSessionAsync("tok123", It.IsAny<CancellationToken>()))
            .ReturnsAsync((DashboardSessionData?)null);

        await m_Events.ValidatePrincipal(context);

        Assert.That(context.Principal, Is.Null);
    }

    [Test]
    public async Task ValidatePrincipal_ValidSession_Proceeds()
    {
        var principal = CreatePrincipal("tok123");
        var context = CreateContext(principal);
        m_MockSessionService.Setup(s => s.GetSessionAsync("tok123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardSessionData(100L, null, DateTime.UtcNow, null, null, null));

        await m_Events.ValidatePrincipal(context);

        Assert.That(context.Principal, Is.Not.Null);
    }

    [Test]
    public async Task ValidatePrincipal_WithAccessToken_Proceeds()
    {
        var principal = CreatePrincipal("tok123");
        var context = CreateContext(principal);
        m_MockSessionService.Setup(s => s.GetSessionAsync("tok123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardSessionData(100L, "access-token", DateTime.UtcNow, null, null, null));

        await m_Events.ValidatePrincipal(context);

        Assert.That(context.Principal, Is.Not.Null);
    }

    [Test]
    public async Task ValidatePrincipal_StaleCookieClaims_RebuiltFromCurrentServerState()
    {
        // Cookie issued before revocation still carries game_write:genshin.
        var principal = CreatePrincipal("tok123",
            new Claim("discord_id", "100"),
            new Claim("perm", "game_write:genshin"));
        var context = CreateContext(principal);
        m_MockSessionService.Setup(s => s.GetSessionAsync("tok123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardSessionData(100L, null, DateTime.UtcNow, null, null, null));
        m_MockUserService.Setup(s => s.GetDashboardUserByDiscordIdAsync(100L, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardUserSummaryDto
            {
                DiscordUserId = "100",
                GameWritePermissions = [Game.HonkaiStarRail]
            });

        await m_Events.ValidatePrincipal(context);

        Assert.That(context.Principal, Is.Not.Null);
        var claims = context.Principal!.Claims.Where(c => c.Type == "perm").Select(c => c.Value).ToList();
        Assert.That(claims, Is.EquivalentTo(["game_write:honkaistarrail"]));
        Assert.That(context.ShouldRenew, Is.True);
    }

    [Test]
    public async Task ValidatePrincipal_AllPermissionsRevoked_KeepsSessionWithoutPermissionClaims()
    {
        var principal = CreatePrincipal("tok123",
            new Claim("discord_id", "100"),
            new Claim(ClaimTypes.Role, "superadmin"),
            new Claim("perm", "game_write:genshin"));
        var context = CreateContext(principal);
        m_MockSessionService.Setup(s => s.GetSessionAsync("tok123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardSessionData(100L, null, DateTime.UtcNow, null, null, null));
        m_MockUserService.Setup(s => s.GetDashboardUserByDiscordIdAsync(100L, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DashboardUserSummaryDto?)null);

        await m_Events.ValidatePrincipal(context);

        // Session itself is still valid (the user can manage their own profiles),
        // but every privilege claim is gone.
        Assert.That(context.Principal, Is.Not.Null);
        Assert.That(context.Principal!.Claims.Where(c => c.Type == "perm"), Is.Empty);
        Assert.That(context.Principal!.IsInRole("superadmin"), Is.False);
        Assert.That(context.Principal!.FindFirst("discord_id")?.Value, Is.EqualTo("100"));
        Assert.That(context.Principal!.FindFirst("dashboard_session")?.Value, Is.EqualTo("tok123"));
    }

    [Test]
    public async Task ValidatePrincipal_UsesSessionIdentityForRebuild()
    {
        // A cookie claiming a different user cannot promote itself: the rebuild
        // always uses the server-side session identity.
        var principal = CreatePrincipal("tok123", new Claim("discord_id", "999"));
        var context = CreateContext(principal);
        m_MockSessionService.Setup(s => s.GetSessionAsync("tok123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardSessionData(100L, null, DateTime.UtcNow, null, null, null));
        m_MockUserService.Setup(s => s.GetDashboardUserByDiscordIdAsync(100L, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardUserSummaryDto
            {
                DiscordUserId = "100",
                IsSuperAdmin = true,
                GameWritePermissions = []
            });

        await m_Events.ValidatePrincipal(context);

        Assert.That(context.Principal, Is.Not.Null);
        Assert.That(context.Principal!.FindFirst("discord_id")?.Value, Is.EqualTo("100"));
        Assert.That(context.Principal!.IsInRole("superadmin"), Is.True);
        m_MockUserService.Verify(s => s.GetDashboardUserByDiscordIdAsync(100L, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region SigningOut

    [Test]
    public async Task ValidatePrincipal_ValidSession_PerformsNoSessionWrites()
    {
        // Validation on the hot path is read-only. Over-limit requests rejected before authentication therefore skip
        // all session storage.
        var principal = CreatePrincipal("tok123");
        var context = CreateContext(principal);
        m_MockSessionService.Setup(s => s.GetSessionAsync("tok123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardSessionData(100L, null, DateTime.UtcNow, null, null, null));

        await m_Events.ValidatePrincipal(context);

        Assert.That(context.Principal, Is.Not.Null);
        m_MockSessionService.Verify(s => s.GetSessionAsync("tok123", It.IsAny<CancellationToken>()), Times.Once);
        m_MockSessionService.Verify(s => s.GetAndRefreshSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        m_MockSessionService.Verify(s => s.RefreshSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

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

    [Test]
    public async Task SigningOut_WithSessionToken_RevokesAllProfileUnlocks()
    {
        // Logout clears every profile unlock so a later session cannot reuse them.
        var principal = CreatePrincipal("tok123", new Claim("discord_id", "100"));
        var httpContext = new DefaultHttpContext { User = principal };
        var scheme = new AuthenticationScheme(
            CookieAuthenticationDefaults.AuthenticationScheme,
            "Test",
            typeof(CookieAuthenticationHandler));
        var context = new CookieSigningOutContext(httpContext, scheme, new CookieAuthenticationOptions(), new AuthenticationProperties(), new CookieOptions());

        await m_Events.SigningOut(context);

        m_MockSessionService.Verify(s => s.InvalidateSessionAsync("tok123", It.IsAny<CancellationToken>()), Times.Once);
        m_MockProfileAuthService.Verify(s => s.RevokeAllAsync(100UL, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task SigningOut_WithoutDiscordClaim_FallsBackToSessionIdentity()
    {
        var principal = CreatePrincipal("tok123");
        var httpContext = new DefaultHttpContext { User = principal };
        var scheme = new AuthenticationScheme(
            CookieAuthenticationDefaults.AuthenticationScheme,
            "Test",
            typeof(CookieAuthenticationHandler));
        var context = new CookieSigningOutContext(httpContext, scheme, new CookieAuthenticationOptions(), new AuthenticationProperties(), new CookieOptions());
        m_MockSessionService.Setup(s => s.GetSessionAsync("tok123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardSessionData(100L, null, DateTime.UtcNow, null, null, null));

        await m_Events.SigningOut(context);

        m_MockProfileAuthService.Verify(s => s.RevokeAllAsync(100UL, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task SigningOut_RevocationFailure_DoesNotThrow()
    {
        var principal = CreatePrincipal("tok123", new Claim("discord_id", "100"));
        var httpContext = new DefaultHttpContext { User = principal };
        var scheme = new AuthenticationScheme(
            CookieAuthenticationDefaults.AuthenticationScheme,
            "Test",
            typeof(CookieAuthenticationHandler));
        var context = new CookieSigningOutContext(httpContext, scheme, new CookieAuthenticationOptions(), new AuthenticationProperties(), new CookieOptions());
        m_MockProfileAuthService.Setup(s => s.RevokeAllAsync(It.IsAny<ulong>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("cache down"));

        Assert.DoesNotThrowAsync(() => m_Events.SigningOut(context));
    }

    #endregion
}


