using Mehrak.Dashboard.Auth;
using Mehrak.Dashboard.Services;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mehrak.Dashboard.Tests.Services;

[TestFixture]
public class DashboardApplicationExecutorServiceTests
{
    private Mock<IServiceProvider> m_ServiceProviderMock;
    private Mock<IDashboardProfileAuthenticationService> m_AuthServiceMock;
    private Mock<ILogger<DashboardApplicationExecutorService<IApplicationContext>>> m_LoggerMock;
    private DashboardApplicationExecutorService<IApplicationContext> m_Service;
    private Mock<IApplicationContext> m_ContextMock;

    [SetUp]
    public void SetUp()
    {
        m_ServiceProviderMock = new Mock<IServiceProvider>();
        m_AuthServiceMock = new Mock<IDashboardProfileAuthenticationService>();
        m_LoggerMock = new Mock<ILogger<DashboardApplicationExecutorService<IApplicationContext>>>();
        m_ContextMock = new Mock<IApplicationContext>();

        m_Service = new DashboardApplicationExecutorService<IApplicationContext>(
            m_ServiceProviderMock.Object,
            m_AuthServiceMock.Object,
            m_LoggerMock.Object
        );
    }

    [Test]
    public void ExecuteAsync_ThrowsIfContextNotProvided()
    {
        // act & assert
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => m_Service.ExecuteAsync(1));
        Assert.That(ex.Message, Does.Contain("ApplicationContext must be provided"));
    }

    [Test]
    public void ExecuteAsync_ThrowsIfDiscordUserIdNotProvided()
    {
        // arrange
        m_Service.ApplicationContext = m_ContextMock.Object;

        // act & assert
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => m_Service.ExecuteAsync(1));
        Assert.That(ex.Message, Does.Contain("Discord user ID must be provided"));
    }

    [Test]
    public async Task ExecuteAsync_ValidationFailed()
    {
        // arrange
        m_Service.ApplicationContext = m_ContextMock.Object;
        m_Service.DiscordUserId = 123;
        m_Service.AddValidator<string>("testParam", s => false, "Test validation error");

        m_ContextMock.Setup(c => c.GetParameter<string>("testParam")).Returns("someValue");

        // act
        var result = await m_Service.ExecuteAsync(1);

        // assert
        Assert.That(result.Status, Is.EqualTo(DashboardExecutionStatus.ValidationFailed));
        Assert.That(result.ValidationErrors, Does.Contain("Test validation error"));
    }

    [Test]
    public async Task ExecuteAsync_AuthSuccess_ExecutesApplication()
    {
        // arrange
        m_Service.ApplicationContext = m_ContextMock.Object;
        m_Service.DiscordUserId = 123;
        uint profileId = 1;
        ulong ltUid = 456;
        string lToken = "ltoken";

        var userDto = new UserDto { Id = 123 };
        var authResult = DashboardProfileAuthenticationResult.Success(userDto, ltUid, lToken);

        m_AuthServiceMock.Setup(s => s.AuthenticateAsync(123, profileId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(authResult);

        var appServiceMock = new Mock<IApplicationService<IApplicationContext>>();
        var commandResult = CommandResult.Success();
        appServiceMock.Setup(s => s.ExecuteAsync(m_ContextMock.Object))
            .ReturnsAsync(commandResult);

        m_ServiceProviderMock.Setup(sp => sp.GetService(typeof(IApplicationService<IApplicationContext>)))
            .Returns(appServiceMock.Object);

        // act
        var result = await m_Service.ExecuteAsync(profileId);

        // assert
        Assert.That(result.Status, Is.EqualTo(DashboardExecutionStatus.Success));
        Assert.That(result.CommandResult, Is.EqualTo(commandResult));
        m_ContextMock.VerifySet(c => c.LtUid = ltUid);
        m_ContextMock.VerifySet(c => c.LToken = lToken);
    }

     [Test]
    public async Task ExecuteAsync_AuthFailed_ReturnsError()
    {
        // arrange
        m_Service.ApplicationContext = m_ContextMock.Object;
        m_Service.DiscordUserId = 123;
        uint profileId = 1;

        var authResult = DashboardProfileAuthenticationResult.InvalidPassphrase("Wrong pass");

        m_AuthServiceMock.Setup(s => s.AuthenticateAsync(123, profileId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(authResult);

        // act
        var result = await m_Service.ExecuteAsync(profileId);

        // assert
        Assert.That(result.Status, Is.EqualTo(DashboardExecutionStatus.AuthenticationFailed));
        Assert.That(result.ErrorMessage, Is.EqualTo("Wrong pass"));
    }
}
