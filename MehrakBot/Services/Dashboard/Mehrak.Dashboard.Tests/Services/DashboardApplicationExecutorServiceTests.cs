using Grpc.Core;
using Mehrak.Dashboard.Auth;
using Mehrak.Dashboard.Services;
using Mehrak.Domain.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Proto = Mehrak.Domain.Protobuf;

namespace Mehrak.Dashboard.Tests.Services;

[TestFixture]
public class DashboardApplicationExecutorServiceTests
{
    private Mock<IServiceProvider> m_ServiceProviderMock;
    private Mock<IDashboardProfileAuthenticationService> m_AuthServiceMock;
    private Mock<ILogger<DashboardApplicationExecutorService>> m_LoggerMock;
    private Mock<Proto.ApplicationService.ApplicationServiceClient> m_ApplicationClientMock;
    private DashboardApplicationExecutorService m_Service;

    [SetUp]
    public void SetUp()
    {
        m_ServiceProviderMock = new Mock<IServiceProvider>();
        m_AuthServiceMock = new Mock<IDashboardProfileAuthenticationService>();
        m_LoggerMock = new Mock<ILogger<DashboardApplicationExecutorService>>();
        m_ApplicationClientMock = new Mock<Proto.ApplicationService.ApplicationServiceClient>();

        m_ServiceProviderMock.Setup(sp => sp.GetService(typeof(Proto.ApplicationService.ApplicationServiceClient)))
            .Returns(m_ApplicationClientMock.Object);

        m_Service = new DashboardApplicationExecutorService(
            m_ServiceProviderMock.Object,
            m_AuthServiceMock.Object,
            m_LoggerMock.Object
        );
    }

    [Test]
    public void ExecuteAsync_ThrowsIfDiscordUserIdNotProvided()
    {
        // arrange
        m_Service.CommandName = "testCommand";

        // act & assert
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => m_Service.ExecuteAsync(1));
        Assert.That(ex.Message, Does.Contain("Discord user ID must be provided"));
    }

    [Test]
    public void ExecuteAsync_ThrowsIfCommandNameNotProvided()
    {
        // arrange
        m_Service.DiscordUserId = 123;
        m_Service.CommandName = "";

        // act & assert
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => m_Service.ExecuteAsync(1));
        Assert.That(ex.Message, Does.Contain("Command name must be provided"));
    }

    [Test]
    public async Task ExecuteAsync_ValidationFailed()
    {
        // arrange
        m_Service.DiscordUserId = 123;
        m_Service.CommandName = "testCommand";
        m_Service.AddValidator<string>("testParam", s => false, "Test validation error");
        m_Service.Parameters = new Dictionary<string, object> { { "testParam", "someValue" } };

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
        m_Service.DiscordUserId = 123;
        m_Service.CommandName = "testCommand";

        var profileId = 1;
        ulong ltUid = 456;
        var lToken = "ltoken";

        var userDto = new UserDto { Id = 123 };
        var authResult = DashboardProfileAuthenticationResult.Success(userDto, ltUid, lToken);

        m_AuthServiceMock.Setup(s => s.AuthenticateAsync(123, profileId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(authResult);

        var commandResultProto = new Proto.CommandResult
        {
            IsSuccess = true,
            Data = new Proto.CommandResultData { IsContainer = false, IsEphemeral = false }
        };
        m_ApplicationClientMock.Setup(c => c.ExecuteCommandAsync(It.IsAny<Proto.ExecuteRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(CreateUnaryCall(commandResultProto));

        // act
        var result = await m_Service.ExecuteAsync(profileId);

        // assert
        Assert.That(result.Status, Is.EqualTo(DashboardExecutionStatus.Success));
        Assert.That(result.CommandResult, Is.Not.Null);
        Assert.That(result.CommandResult!.IsSuccess, Is.True);

        m_ApplicationClientMock.Verify(c => c.ExecuteCommandAsync(
            It.Is<Proto.ExecuteRequest>(req =>
                req.DiscordUserId == 123 &&
                req.CommandName == "testCommand" &&
                req.LtUid == ltUid &&
                req.LToken == lToken),
            null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_AuthFailed_ReturnsError()
    {
        // arrange
        m_Service.DiscordUserId = 123;
        m_Service.CommandName = "testCommand";
        var profileId = 1;

        var authResult = DashboardProfileAuthenticationResult.InvalidPassphrase("Wrong pass");

        m_AuthServiceMock.Setup(s => s.AuthenticateAsync(123, profileId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(authResult);

        // act
        var result = await m_Service.ExecuteAsync(profileId);

        // assert
        Assert.That(result.Status, Is.EqualTo(DashboardExecutionStatus.AuthenticationFailed));
        Assert.That(result.ErrorMessage, Is.EqualTo("Wrong pass"));

        m_ApplicationClientMock.Verify(c => c.ExecuteCommandAsync(It.IsAny<Proto.ExecuteRequest>(), null, null, It.IsAny<CancellationToken>()), Times.Never);
    }

    private static AsyncUnaryCall<Proto.CommandResult> CreateUnaryCall(Proto.CommandResult result)
    {
        return new AsyncUnaryCall<Proto.CommandResult>(
            Task.FromResult(result),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => [],
            () => { });
    }
}
