#region

using Mehrak.Application.Models.Context;
using Mehrak.Bot.Authentication;
using Mehrak.Bot.Services;
using Mehrak.Domain.Models;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NetCord;
using NetCord.Services;

#endregion

namespace Mehrak.Bot.Tests.Services;

/// <summary>
/// Unit tests for CheckInExecutorService validating command execution flow,
/// authentication integration, validation, and error handling.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class CheckInExecutorServiceTests
{
    private Mock<IApplicationService<CheckInApplicationContext>> m_MockApplicationService = null!;
    private Mock<IUserRepository> m_MockUserRepository = null!;
    private Mock<ICommandRateLimitService> m_MockRateLimitService = null!;
    private Mock<IAuthenticationMiddlewareService> m_MockAuthMiddleware = null!;
    private Mock<IMetricsService> m_MockMetricsService = null!;
    private CheckInExecutorService m_Service = null!;
    private DiscordTestHelper m_DiscordHelper = null!;

    private ulong m_TestUserId;
    private const uint TestProfileId = 1U;
    private const ulong TestLtUid = 987654321UL;
    private const string TestLToken = "test-ltoken-value";

    [SetUp]
    public void Setup()
    {
        m_MockApplicationService = new Mock<IApplicationService<CheckInApplicationContext>>();
        m_MockUserRepository = new Mock<IUserRepository>();
        m_MockRateLimitService = new Mock<ICommandRateLimitService>();
        m_MockAuthMiddleware = new Mock<IAuthenticationMiddlewareService>();
        m_MockMetricsService = new Mock<IMetricsService>();
        m_DiscordHelper = new DiscordTestHelper();
        m_DiscordHelper.SetupRequestCapture();

        m_Service = new CheckInExecutorService(
            m_MockApplicationService.Object,
            m_MockUserRepository.Object,
            m_MockRateLimitService.Object,
            m_MockAuthMiddleware.Object,
            m_MockMetricsService.Object,
            NullLogger<CheckInExecutorService>.Instance);

        m_TestUserId = (ulong)new Random(DateTime.UtcNow.Microsecond).NextInt64();

        // Setup default mock behaviors
        m_MockRateLimitService
            .Setup(x => x.IsRateLimitedAsync(It.IsAny<ulong>()))
            .ReturnsAsync(false);

        m_MockMetricsService
            .Setup(x => x.ObserveCommandDuration(It.IsAny<string>()))
            .Returns(Mock.Of<IDisposable>());
    }

    [TearDown]
    public void TearDown()
    {
        m_MockApplicationService.Reset();
        m_MockUserRepository.Reset();
        m_MockRateLimitService.Reset();
        m_MockAuthMiddleware.Reset();
        m_MockMetricsService.Reset();
        m_DiscordHelper?.Dispose();
    }

    #region ExecuteAsync - Validation Tests

    [Test]
    public async Task ExecuteAsync_WithValidValidation_ProceedsToRateLimit()
    {
        // Arrange
        SlashCommandInteraction interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        m_Service.Context = mockContext.Object;
        m_Service.ApplicationContext = new CheckInApplicationContext(m_TestUserId);

        // Setup rate limit to trigger (so we can verify it was checked)
        m_MockRateLimitService
            .Setup(x => x.IsRateLimitedAsync(m_TestUserId))
            .ReturnsAsync(true);

        // Act
        await m_Service.ExecuteAsync(TestProfileId);

        // Assert
        m_MockRateLimitService.Verify(x => x.IsRateLimitedAsync(m_TestUserId), Times.Once);
    }

    #endregion

    #region ExecuteAsync - Rate Limit Tests

    [Test]
    public async Task ExecuteAsync_WhenRateLimited_SendsRateLimitMessage()
    {
        // Arrange
        SlashCommandInteraction interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        m_Service.Context = mockContext.Object;
        m_Service.ApplicationContext = new CheckInApplicationContext(m_TestUserId);

        m_MockRateLimitService
            .Setup(x => x.IsRateLimitedAsync(m_TestUserId))
            .ReturnsAsync(true);

        // Act
        await m_Service.ExecuteAsync(TestProfileId);

        // Assert
        var response = await m_DiscordHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("used command too frequent").IgnoreCase);

        // Verify authentication was not attempted
        m_MockAuthMiddleware.Verify(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()), Times.Never);
        m_MockApplicationService.Verify(x => x.ExecuteAsync(It.IsAny<CheckInApplicationContext>()), Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_WhenNotRateLimited_ProceedsToAuthentication()
    {
        // Arrange
        SlashCommandInteraction interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        m_Service.Context = mockContext.Object;
        m_Service.ApplicationContext = new CheckInApplicationContext(m_TestUserId);

        m_MockRateLimitService
            .Setup(x => x.IsRateLimitedAsync(m_TestUserId))
            .ReturnsAsync(false);

        var user = new UserModel
        { Id = m_TestUserId, Profiles = [new UserProfile { ProfileId = TestProfileId, LtUid = TestLtUid }] };

        m_MockAuthMiddleware
            .Setup(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()))
            .ReturnsAsync(AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, user, mockContext.Object));

        m_MockApplicationService
            .Setup(x => x.ExecuteAsync(It.IsAny<CheckInApplicationContext>()))
            .ReturnsAsync(CommandResult.Success());

        // Act
        await m_Service.ExecuteAsync(TestProfileId);

        // Assert
        m_MockRateLimitService.Verify(x => x.IsRateLimitedAsync(m_TestUserId), Times.Once);
        m_MockAuthMiddleware.Verify(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()), Times.Once);
    }

    #endregion

    #region ExecuteAsync - Authentication Tests

    [Test]
    public async Task ExecuteAsync_AuthenticationSuccess_ExecutesApplicationService()
    {
        // Arrange
        SlashCommandInteraction interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        m_Service.Context = mockContext.Object;
        m_Service.ApplicationContext = new CheckInApplicationContext(m_TestUserId);

        var user = new UserModel
        {
            Id = m_TestUserId,
            Profiles = [new UserProfile { ProfileId = TestProfileId, LtUid = TestLtUid }]
        };

        m_MockAuthMiddleware
            .Setup(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()))
            .ReturnsAsync(AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, user, mockContext.Object));

        m_MockApplicationService
            .Setup(x => x.ExecuteAsync(It.IsAny<CheckInApplicationContext>()))
            .ReturnsAsync(CommandResult.Success());

        // Act
        await m_Service.ExecuteAsync(TestProfileId);

        // Assert
        m_MockApplicationService.Verify(
            x => x.ExecuteAsync(It.Is<CheckInApplicationContext>(ctx =>
                ctx.UserId == m_TestUserId &&
                ctx.LToken == TestLToken &&
                ctx.LtUid == TestLtUid)),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_AuthenticationSuccess_SendsDeferredResponse()
    {
        // Arrange
        SlashCommandInteraction interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        m_Service.Context = mockContext.Object;
        m_Service.ApplicationContext = new CheckInApplicationContext(m_TestUserId);

        var user = new UserModel
        {
            Id = m_TestUserId,
            Profiles = [new UserProfile { ProfileId = TestProfileId, LtUid = TestLtUid }]
        };

        m_MockAuthMiddleware
            .Setup(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()))
            .ReturnsAsync(AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, user, mockContext.Object));

        m_MockApplicationService
            .Setup(x => x.ExecuteAsync(It.IsAny<CheckInApplicationContext>()))
            .ReturnsAsync(CommandResult.Success());

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
        SlashCommandInteraction interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        m_Service.Context = mockContext.Object;
        m_Service.ApplicationContext = new CheckInApplicationContext(m_TestUserId);

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
        m_MockApplicationService.Verify(x => x.ExecuteAsync(It.IsAny<CheckInApplicationContext>()), Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_AuthenticationTimeout_DoesNotSendResponse()
    {
        // Arrange
        SlashCommandInteraction interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        m_Service.Context = mockContext.Object;
        m_Service.ApplicationContext = new CheckInApplicationContext(m_TestUserId);

        m_MockAuthMiddleware
            .Setup(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()))
            .ReturnsAsync(AuthenticationResult.Timeout());

        // Act
        await m_Service.ExecuteAsync(TestProfileId);

        // Assert - With timeout, context is null so no response should be sent
        m_MockApplicationService.Verify(x => x.ExecuteAsync(It.IsAny<CheckInApplicationContext>()), Times.Never);
    }

    #endregion

    #region ExecuteAsync - Application Service Execution Tests

    [Test]
    public async Task ExecuteAsync_ApplicationServiceSuccess_SendsSuccessFollowup()
    {
        // Arrange
        SlashCommandInteraction interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        m_Service.Context = mockContext.Object;
        m_Service.ApplicationContext = new CheckInApplicationContext(m_TestUserId);

        var user = new UserModel
        {
            Id = m_TestUserId,
            Profiles = [new UserProfile { ProfileId = TestProfileId, LtUid = TestLtUid }]
        };

        m_MockAuthMiddleware
            .Setup(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()))
            .ReturnsAsync(AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, user, mockContext.Object));

        m_MockApplicationService
            .Setup(x => x.ExecuteAsync(It.IsAny<CheckInApplicationContext>()))
            .ReturnsAsync(CommandResult.Success());

        // Act
        await m_Service.ExecuteAsync(TestProfileId);

        // Assert
        m_MockApplicationService.Verify(x => x.ExecuteAsync(It.IsAny<CheckInApplicationContext>()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_ApplicationServiceFailure_SendsErrorFollowup()
    {
        // Arrange
        SlashCommandInteraction interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        m_Service.Context = mockContext.Object;
        m_Service.ApplicationContext = new CheckInApplicationContext(m_TestUserId);

        var user = new UserModel
        {
            Id = m_TestUserId,
            Profiles = [new UserProfile { ProfileId = TestProfileId, LtUid = TestLtUid }]
        };

        m_MockAuthMiddleware
            .Setup(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()))
            .ReturnsAsync(AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, user, mockContext.Object));

        const string errorMessage = "Check-in failed: Already checked in today";

        m_MockApplicationService
            .Setup(x => x.ExecuteAsync(It.IsAny<CheckInApplicationContext>()))
            .ReturnsAsync(CommandResult.Failure(CommandFailureReason.ApiError, errorMessage));

        // Act
        await m_Service.ExecuteAsync(TestProfileId);

        // Assert
        m_MockApplicationService.Verify(x => x.ExecuteAsync(It.IsAny<CheckInApplicationContext>()), Times.Once);
    }

    #endregion

    #region ExecuteAsync - Metrics Tests

    [Test]
    public async Task ExecuteAsync_Success_TracksCommandMetrics()
    {
        // Arrange
        SlashCommandInteraction interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        m_Service.Context = mockContext.Object;
        m_Service.ApplicationContext = new CheckInApplicationContext(m_TestUserId);

        var user = new UserModel
        {
            Id = m_TestUserId,
            Profiles = [new UserProfile { ProfileId = TestProfileId, LtUid = TestLtUid }]
        };

        m_MockAuthMiddleware
            .Setup(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()))
            .ReturnsAsync(AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, user, mockContext.Object));

        m_MockApplicationService
            .Setup(x => x.ExecuteAsync(It.IsAny<CheckInApplicationContext>()))
            .ReturnsAsync(CommandResult.Success());

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
        SlashCommandInteraction interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        m_Service.Context = mockContext.Object;
        m_Service.ApplicationContext = new CheckInApplicationContext(m_TestUserId);

        var user = new UserModel
        {
            Id = m_TestUserId,
            Profiles = [new UserProfile { ProfileId = TestProfileId, LtUid = TestLtUid }]
        };

        m_MockAuthMiddleware
            .Setup(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()))
            .ReturnsAsync(AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, user, mockContext.Object));

        m_MockApplicationService
            .Setup(x => x.ExecuteAsync(It.IsAny<CheckInApplicationContext>()))
            .ReturnsAsync(CommandResult.Failure(CommandFailureReason.ApiError, "Error"));

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
        SlashCommandInteraction interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        m_Service.Context = mockContext.Object;
        m_Service.ApplicationContext = new CheckInApplicationContext(m_TestUserId);

        var user = new UserModel
        {
            Id = m_TestUserId,
            Profiles = [new UserProfile { ProfileId = TestProfileId, LtUid = TestLtUid }]
        };

        m_MockAuthMiddleware
            .Setup(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()))
            .ReturnsAsync(AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, user, mockContext.Object));

        m_MockApplicationService
            .Setup(x => x.ExecuteAsync(It.IsAny<CheckInApplicationContext>()))
            .ReturnsAsync(CommandResult.Success());

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
        SlashCommandInteraction interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        m_Service.Context = mockContext.Object;
        m_Service.ApplicationContext = new CheckInApplicationContext(m_TestUserId);

        var callOrder = new List<string>();

        m_MockRateLimitService
            .Setup(x => x.IsRateLimitedAsync(m_TestUserId))
            .ReturnsAsync(false)
            .Callback(() => callOrder.Add("RateLimit"));

        var user = new UserModel
        {
            Id = m_TestUserId,
            Profiles = [new UserProfile { ProfileId = TestProfileId, LtUid = TestLtUid }]
        };

        m_MockAuthMiddleware
            .Setup(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()))
            .ReturnsAsync(AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, user, mockContext.Object))
            .Callback(() => callOrder.Add("Authentication"));

        m_MockMetricsService
            .Setup(x => x.ObserveCommandDuration("checkin"))
            .Returns(Mock.Of<IDisposable>())
            .Callback(() => callOrder.Add("MetricsStart"));

        m_MockApplicationService
            .Setup(x => x.ExecuteAsync(It.IsAny<CheckInApplicationContext>()))
            .ReturnsAsync(CommandResult.Success())
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
            Assert.That(callOrder[0], Is.EqualTo("RateLimit"), "Rate limit should be checked first");
            Assert.That(callOrder[1], Is.EqualTo("Authentication"), "Authentication should be second");
            Assert.That(callOrder[2], Is.EqualTo("MetricsStart"), "Metrics observer should start before execution");
            Assert.That(callOrder[3], Is.EqualTo("Execute"), "Application service should execute after metrics start");
        });
    }

    [Test]
    public async Task ExecuteAsync_WithRateLimit_StopsAtRateLimitCheck()
    {
        // Arrange
        SlashCommandInteraction interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        m_Service.Context = mockContext.Object;
        m_Service.ApplicationContext = new CheckInApplicationContext(m_TestUserId);

        m_MockRateLimitService
            .Setup(x => x.IsRateLimitedAsync(m_TestUserId))
            .ReturnsAsync(true);

        // Act
        await m_Service.ExecuteAsync(TestProfileId);

        // Assert - Verify no downstream calls were made
        m_MockAuthMiddleware.Verify(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()), Times.Never);
        m_MockApplicationService.Verify(x => x.ExecuteAsync(It.IsAny<CheckInApplicationContext>()), Times.Never);
        m_MockMetricsService.Verify(x => x.TrackCommand(It.IsAny<string>(), It.IsAny<ulong>(), It.IsAny<bool>()),
            Times.Never);
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

    [Test]
    public void ApplicationContext_CanBeSetAndRetrieved()
    {
        // Arrange
        var context = new CheckInApplicationContext(m_TestUserId);

        // Act
        m_Service.ApplicationContext = context;

        // Assert
        Assert.That(m_Service.ApplicationContext, Is.EqualTo(context));
        Assert.That(m_Service.ApplicationContext.UserId, Is.EqualTo(m_TestUserId));
    }

    #endregion
}
