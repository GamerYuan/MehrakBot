using System.Security.Claims;
using Mehrak.Dashboard.Auth;
using Mehrak.Domain.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mehrak.Dashboard.Tests.Auth;

[TestFixture]
public class AuthControllerTests
{
    private Mock<IDashboardAuthService> m_MockAuthService = null!;
    private Mock<ILogger<AuthController>> m_MockLogger = null!;
    private IConfiguration m_Config = null!;
    private AuthController m_Controller = null!;

    [SetUp]
    public void SetUp()
    {
        m_MockAuthService = new Mock<IDashboardAuthService>();
        m_MockLogger = new Mock<ILogger<AuthController>>();
        m_Config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Dashboard:Origin", "https://dashboard.example.com" }
            })
            .Build();

        m_Controller = new AuthController(m_MockAuthService.Object, m_MockLogger.Object, m_Config);
    }

    private void SetupHttpContext(long? discordId = null, string? sessionToken = null, bool isSuperAdmin = false)
    {
        var claims = new List<Claim>();
        if (discordId.HasValue)
            claims.Add(new Claim("discord_id", discordId.Value.ToString()));
        if (sessionToken != null)
            claims.Add(new Claim("dashboard_session", sessionToken));
        if (isSuperAdmin)
            claims.Add(new Claim(ClaimTypes.Role, "superadmin"));

        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext
        {
            User = principal
        };

        m_Controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
    }

    #region Discord

    [Test]
    public void Discord_ReturnsChallengeResult()
    {
        m_Controller.Url = new Mock<IUrlHelper>().Object;

        var result = m_Controller.Discord();

        Assert.That(result, Is.InstanceOf<ChallengeResult>());
    }

    #endregion

    #region Logout

    [Test]
    public async Task Logout_WithValidUser_ReturnsOk()
    {
        SetupHttpContext(discordId: 100L);

        var authServiceMock = new Mock<IAuthenticationService>();
        authServiceMock.Setup(a => a.SignOutAsync(It.IsAny<HttpContext>(), It.IsAny<string>(),
                It.IsAny<AuthenticationProperties>()))
            .Returns(Task.CompletedTask);

        var sp = new Mock<IServiceProvider>();
        sp.Setup(s => s.GetService(typeof(IAuthenticationService))).Returns(authServiceMock.Object);
        m_Controller.ControllerContext.HttpContext.RequestServices = sp.Object;

        var result = await m_Controller.Logout();

        Assert.That(result, Is.InstanceOf<OkResult>());
    }

    #endregion
}
