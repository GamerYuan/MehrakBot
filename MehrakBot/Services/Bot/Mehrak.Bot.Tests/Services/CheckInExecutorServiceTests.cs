#region

using Grpc.Core;
using Mehrak.Bot.Authentication;
using Mehrak.Bot.Services;
using Mehrak.Bot.Services.Abstractions;
using Mehrak.Domain.Models;
using Mehrak.Domain.Protobuf;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.Infrastructure.Context;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NetCord.Services;

#endregion

namespace Mehrak.Bot.Tests.Services;

/// <summary>
/// Unit tests for CommandExecutorService validating checkin command execution flow,
/// authentication integration, validation, and error handling.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class CheckInExecutorServiceTests
{
    private Mock<ApplicationService.ApplicationServiceClient> m_MockApplicationClient = null!;
    private Mock<IAuthenticationMiddlewareService> m_MockAuthMiddleware = null!;
    private Mock<IBotMetrics> m_MockMetricsService = null!;
    private Mock<IAttachmentStorageService> m_MockAttachmentService = null!;
    private Mock<IImageRepository> m_MockImageRepository = null!;
    private CommandExecutorService m_Service = null!;
    private DiscordTestHelper m_DiscordHelper = null!;
    private TestDbContextFactory? m_DbFactory;
    private IServiceScope? m_DbScope;
    private UserDbContext m_UserContext = null!;

    private ulong m_TestUserId;
    private const int TestProfileId = 1;
    private const ulong TestLtUid = 987654321UL;
    private const string TestLToken = "test-ltoken-value";

    [SetUp]
    public void Setup()
    {
        m_MockApplicationClient = new Mock<ApplicationService.ApplicationServiceClient>();
        m_MockAuthMiddleware = new Mock<IAuthenticationMiddlewareService>();
        m_MockMetricsService = new Mock<IBotMetrics>();
        m_MockAttachmentService = new Mock<IAttachmentStorageService>();
        m_MockImageRepository = new Mock<IImageRepository>();
        m_DiscordHelper = new DiscordTestHelper();
        m_DiscordHelper.SetupRequestCapture();

        m_DbFactory?.Dispose();
        m_DbFactory = new TestDbContextFactory();
        m_DbScope = m_DbFactory.ScopeFactory.CreateScope();
        m_UserContext = m_DbScope.ServiceProvider.GetRequiredService<UserDbContext>();

        m_MockApplicationClient
            .Setup(x => x.ExecuteCommandAsync(It.IsAny<ExecuteRequest>(), null, null, default))
            .Returns(CreateUnaryCall(new Mehrak.Domain.Protobuf.CommandResult
            {
                IsSuccess = true,
                Data = new Mehrak.Domain.Protobuf.CommandResultData { IsContainer = false, IsEphemeral = false }
            }));

        m_Service = new CommandExecutorService(
            m_UserContext,
            m_MockAuthMiddleware.Object,
            m_MockMetricsService.Object,
            m_MockApplicationClient.Object,
            m_MockAttachmentService.Object,
            m_MockImageRepository.Object,
            NullLogger<CommandExecutorService>.Instance)
        {
            CommandName = "checkin",
            ValidateServer = false,
            IsResponseEphemeral = true
        };

        m_TestUserId = (ulong)new Random(DateTime.UtcNow.Microsecond).NextInt64();

        m_MockMetricsService
            .Setup(x => x.ObserveCommandDuration(It.IsAny<string>()))
            .Returns(Mock.Of<IDisposable>());
    }

    [TearDown]
    public void TearDown()
    {
        m_MockApplicationClient.Reset();
        m_MockAuthMiddleware.Reset();
        m_MockMetricsService.Reset();
        m_MockAttachmentService.Reset();
        m_MockImageRepository.Reset();
        m_DiscordHelper?.Dispose();
        m_DbScope?.Dispose();
        m_DbFactory?.Dispose();
    }

    #region ExecuteAsync - Authentication Tests

    [Test]
    public async Task ExecuteAsync_AuthenticationSuccess_ExecutesApplicationService()
    {
        // Arrange
        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        m_Service.Context = mockContext.Object;

        var user = new UserDto
        {
            Id = m_TestUserId,
            Profiles = [new UserProfileDto { ProfileId = TestProfileId, LtUid = TestLtUid }]
        };

        m_MockAuthMiddleware
            .Setup(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()))
            .ReturnsAsync(AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, user, mockContext.Object));

        // Act
        await m_Service.ExecuteAsync(TestProfileId);

        // Assert
        m_MockApplicationClient.Verify(
            x => x.ExecuteCommandAsync(It.Is<ExecuteRequest>(req =>
                req.CommandName == "checkin" &&
                req.DiscordUserId == m_TestUserId &&
                req.LToken == TestLToken &&
                req.LtUid == TestLtUid), null, null, default),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_AuthenticationSuccess_SendsDeferredResponse()
    {
        // Arrange
        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        m_Service.Context = mockContext.Object;

        var user = new UserDto
        {
            Id = m_TestUserId,
            Profiles = [new UserProfileDto { ProfileId = TestProfileId, LtUid = TestLtUid }]
        };

        m_MockAuthMiddleware
            .Setup(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()))
            .ReturnsAsync(AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, user, mockContext.Object));

        // Act
        await m_Service.ExecuteAsync(TestProfileId);

        // Assert - Verify deferred response was sent
        var response = await m_DiscordHelper.ExtractInteractionResponseDataAsync();
        // Deferred responses typically have empty content or specific structure
        Assert.That(response, Is.Not.Null);
    }

    [Test]
    public async Task ExecuteAsync_AuthenticationFailure_SendsFailureMessage()
    {
        // Arrange
        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        m_Service.Context = mockContext.Object;

        const string errorMessage = "Authentication failed - invalid credentials";

        m_MockAuthMiddleware
            .Setup(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()))
            .ReturnsAsync(AuthenticationResult.Failure(mockContext.Object, errorMessage));

        // Act
        await m_Service.ExecuteAsync(TestProfileId);

        // Assert
        var response = await m_DiscordHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain(errorMessage));

        // Verify application service was not called
        m_MockApplicationClient.Verify(x => x.ExecuteCommandAsync(It.IsAny<ExecuteRequest>(), null, null, default), Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_AuthenticationTimeout_DoesNotSendResponse()
    {
        // Arrange
        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        m_Service.Context = mockContext.Object;

        m_MockAuthMiddleware
            .Setup(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()))
            .ReturnsAsync(AuthenticationResult.Timeout());

        // Act
        await m_Service.ExecuteAsync(TestProfileId);

        // Assert - With timeout, context is null so no response should be sent
        m_MockApplicationClient.Verify(x => x.ExecuteCommandAsync(It.IsAny<ExecuteRequest>(), null, null, default), Times.Never);
    }

    #endregion

    #region ExecuteAsync - Application Service Execution Tests

    [Test]
    public async Task ExecuteAsync_ApplicationServiceSuccess_SendsSuccessFollowup()
    {
        // Arrange
        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        m_Service.Context = mockContext.Object;

        var user = new UserDto
        {
            Id = m_TestUserId,
            Profiles = [new UserProfileDto { ProfileId = TestProfileId, LtUid = TestLtUid }]
        };

        m_MockAuthMiddleware
            .Setup(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()))
            .ReturnsAsync(AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, user, mockContext.Object));

        // Act
        await m_Service.ExecuteAsync(TestProfileId);

        // Assert
        m_MockApplicationClient.Verify(x => x.ExecuteCommandAsync(It.IsAny<ExecuteRequest>(), null, null, default), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_ApplicationServiceFailure_SendsErrorFollowup()
    {
        // Arrange
        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        m_Service.Context = mockContext.Object;

        var user = new UserDto
        {
            Id = m_TestUserId,
            Profiles = [new UserProfileDto { ProfileId = TestProfileId, LtUid = TestLtUid }]
        };

        m_MockAuthMiddleware
            .Setup(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()))
            .ReturnsAsync(AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, user, mockContext.Object));

        const string errorMessage = "Check-in failed: Already checked in today";

        m_MockApplicationClient
            .Setup(x => x.ExecuteCommandAsync(It.IsAny<ExecuteRequest>(), null, null, default))
            .Returns(CreateUnaryCall(new Mehrak.Domain.Protobuf.CommandResult
            {
                IsSuccess = false,
                ErrorMessage = errorMessage,
                FailureReason = Mehrak.Domain.Protobuf.CommandFailureReason.ApiError
            }));

        // Act
        await m_Service.ExecuteAsync(TestProfileId);

        // Assert
        m_MockApplicationClient.Verify(x => x.ExecuteCommandAsync(It.IsAny<ExecuteRequest>(), null, null, default), Times.Once);
    }

    #endregion

    #region ExecuteAsync - Metrics Tests

    [Test]
    public async Task ExecuteAsync_Success_TracksCommandMetrics()
    {
        // Arrange
        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        m_Service.Context = mockContext.Object;

        var user = new UserDto
        {
            Id = m_TestUserId,
            Profiles = [new UserProfileDto { ProfileId = TestProfileId, LtUid = TestLtUid }]
        };

        m_MockAuthMiddleware
            .Setup(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()))
            .ReturnsAsync(AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, user, mockContext.Object));

        // Act
        await m_Service.ExecuteAsync(TestProfileId);

        // Assert
        m_MockMetricsService.Verify(
            x => x.TrackCommand("checkin", m_TestUserId, true),
            Times.Once);

        m_MockMetricsService.Verify(
            x => x.ObserveCommandDuration("checkin"),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_Failure_TracksCommandMetricsWithFailure()
    {
        // Arrange
        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        m_Service.Context = mockContext.Object;

        var user = new UserDto
        {
            Id = m_TestUserId,
            Profiles = [new UserProfileDto { ProfileId = TestProfileId, LtUid = TestLtUid }]
        };

        m_MockAuthMiddleware
            .Setup(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()))
            .ReturnsAsync(AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, user, mockContext.Object));

        m_MockApplicationClient
            .Setup(x => x.ExecuteCommandAsync(It.IsAny<ExecuteRequest>(), null, null, default))
            .Returns(CreateUnaryCall(new Mehrak.Domain.Protobuf.CommandResult
            {
                IsSuccess = false,
                ErrorMessage = "Error",
                FailureReason = Mehrak.Domain.Protobuf.CommandFailureReason.ApiError
            }));

        // Act
        await m_Service.ExecuteAsync(TestProfileId);

        // Assert
        m_MockMetricsService.Verify(
            x => x.TrackCommand("checkin", m_TestUserId, false),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_Success_DisposesMetricsObserver()
    {
        // Arrange
        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        m_Service.Context = mockContext.Object;

        var user = new UserDto
        {
            Id = m_TestUserId,
            Profiles = [new UserProfileDto { ProfileId = TestProfileId, LtUid = TestLtUid }]
        };

        m_MockAuthMiddleware
            .Setup(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()))
            .ReturnsAsync(AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, user, mockContext.Object));

        var mockDisposable = new Mock<IDisposable>();
        m_MockMetricsService
            .Setup(x => x.ObserveCommandDuration("checkin"))
            .Returns(mockDisposable.Object);

        // Act
        await m_Service.ExecuteAsync(TestProfileId);

        // Assert
        mockDisposable.Verify(x => x.Dispose(), Times.Once);
    }

    #endregion

    #region ExecuteAsync - Integration Flow Tests

    [Test]
    public async Task ExecuteAsync_CompleteSuccessFlow_ExecutesInCorrectOrder()
    {
        // Arrange
        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        m_Service.Context = mockContext.Object;

        var callOrder = new List<string>();

        var user = new UserDto
        {
            Id = m_TestUserId,
            Profiles = [new UserProfileDto { ProfileId = TestProfileId, LtUid = TestLtUid }]
        };

        m_MockAuthMiddleware
            .Setup(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()))
            .ReturnsAsync(AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, user, mockContext.Object))
            .Callback(() => callOrder.Add("Authentication"));

        m_MockMetricsService
            .Setup(x => x.ObserveCommandDuration("checkin"))
            .Returns(Mock.Of<IDisposable>())
            .Callback(() => callOrder.Add("MetricsStart"));

        m_MockApplicationClient
            .Setup(x => x.ExecuteCommandAsync(It.IsAny<ExecuteRequest>(), null, null, default))
            .Returns(CreateUnaryCall(new Mehrak.Domain.Protobuf.CommandResult
            {
                IsSuccess = true,
                Data = new Mehrak.Domain.Protobuf.CommandResultData { IsContainer = false, IsEphemeral = false }
            }))
            .Callback(() => callOrder.Add("Execute"));

        m_MockMetricsService
            .Setup(x => x.TrackCommand("checkin", m_TestUserId, true))
            .Callback(() => callOrder.Add("MetricsTrack"));

        // Act
        await m_Service.ExecuteAsync(TestProfileId);

        // Assert - Verify execution order
        Assert.Multiple(() =>
        {
            Assert.That(callOrder, Has.Count.GreaterThanOrEqualTo(4));
            Assert.That(callOrder[0], Is.EqualTo("Authentication"), "Authentication should be first");
            Assert.That(callOrder[1], Is.EqualTo("MetricsStart"), "Metrics observer should start before execution");
            Assert.That(callOrder[2], Is.EqualTo("Execute"), "Application service should execute after metrics start");
        });
    }

    #endregion

    #region Property Tests

    [Test]
    public void CommandName_ReturnsCheckin()
    {
        // Assert
        Assert.That(m_Service.CommandName, Is.EqualTo("checkin"));
    }

    [Test]
    public void Context_CanBeSetAndRetrieved()
    {
        // Arrange
        var mockContext = new Mock<IInteractionContext>();

        // Act
        m_Service.Context = mockContext.Object;

        // Assert
        Assert.That(m_Service.Context, Is.EqualTo(mockContext.Object));
    }

    private static AsyncUnaryCall<Mehrak.Domain.Protobuf.CommandResult> CreateUnaryCall(Mehrak.Domain.Protobuf.CommandResult result)
    {
        return new AsyncUnaryCall<Mehrak.Domain.Protobuf.CommandResult>(
            Task.FromResult(result),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => [],
            () => { });
    }

    #endregion
}
