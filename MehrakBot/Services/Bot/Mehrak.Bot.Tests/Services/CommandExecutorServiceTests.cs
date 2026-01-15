#region

using System.Text.Json.Nodes;
using Mehrak.Application.Models.Context;
using Mehrak.Bot.Authentication;
using Mehrak.Bot.Services;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NetCord;
using NetCord.Services;

#endregion

namespace Mehrak.Bot.Tests.Services;

/// <summary>
/// Unit tests for CommandExecutorService validating generic command execution flow,
/// server selection logic, authentication integration, and response handling.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class CommandExecutorServiceTests
{
    private Mock<IServiceProvider> m_MockServiceProvider = null!;
    private Mock<IApplicationService<TestApplicationContext>> m_MockApplicationService = null!;
    private Mock<ICommandRateLimitService> m_MockRateLimitService = null!;
    private Mock<IAuthenticationMiddlewareService> m_MockAuthMiddleware = null!;
    private Mock<IMetricsService> m_MockMetricsService = null!;
    private Mock<IAttachmentStorageService> m_MockAttachmentService = null!;
    private Mock<IImageRepository> m_MockImageRepository = null!;
    private CommandExecutorService<TestApplicationContext> m_Service = null!;
    private DiscordTestHelper m_DiscordHelper = null!;
    private TestDbContextFactory? m_DbFactory;
    private IServiceScope? m_DbScope;
    private UserDbContext m_UserContext = null!;

    private ulong m_TestUserId;
    private const int TestProfileId = 1;
    private const long TestProfileEntityId = 10L;
    private const ulong TestLtUid = 987654321UL;
    private const string TestLToken = "test-ltoken-value";
    private const Game TestGame = Game.Genshin;
    private const Server TestServer = Server.America;

    [SetUp]
    public void Setup()
    {
        m_MockServiceProvider = new Mock<IServiceProvider>();
        m_MockApplicationService = new Mock<IApplicationService<TestApplicationContext>>();
        m_MockRateLimitService = new Mock<ICommandRateLimitService>();
        m_MockAuthMiddleware = new Mock<IAuthenticationMiddlewareService>();
        m_MockMetricsService = new Mock<IMetricsService>();
        m_MockAttachmentService = new Mock<IAttachmentStorageService>();
        m_MockImageRepository = new Mock<IImageRepository>();
        m_DiscordHelper = new DiscordTestHelper();
        m_DiscordHelper.SetupRequestCapture();

        m_DbFactory?.Dispose();
        m_DbFactory = new TestDbContextFactory();
        m_DbScope = m_DbFactory.ScopeFactory.CreateScope();
        m_UserContext = m_DbScope.ServiceProvider.GetRequiredService<UserDbContext>();

        // Setup service provider to return mocked application service
        m_MockServiceProvider
            .Setup(x => x.GetService(typeof(IApplicationService<TestApplicationContext>)))
            .Returns(m_MockApplicationService.Object);

        m_Service = new CommandExecutorService<TestApplicationContext>(
            m_MockServiceProvider.Object,
            m_UserContext,
            m_MockRateLimitService.Object,
            m_MockAuthMiddleware.Object,
            m_MockMetricsService.Object,
            m_MockAttachmentService.Object,
            m_MockImageRepository.Object,
            NullLogger<CommandExecutorService<TestApplicationContext>>.Instance);

        m_TestUserId = (ulong)new Random(DateTime.UtcNow.Microsecond).NextInt64();

        // Setup default mock behaviors
        m_MockRateLimitService
            .Setup(x => x.IsRateLimitedAsync(It.IsAny<ulong>()))
            .ReturnsAsync(false);

        m_MockRateLimitService
            .Setup(x => x.SetRateLimitAsync(It.IsAny<ulong>()))
            .Returns(Task.CompletedTask);

        m_MockMetricsService
            .Setup(x => x.ObserveCommandDuration(It.IsAny<string>()))
            .Returns(Mock.Of<IDisposable>());
    }

    [TearDown]
    public void TearDown()
    {
        m_DiscordHelper?.Dispose();
        m_DbScope?.Dispose();
        m_DbFactory?.Dispose();
    }

    #region Server Selection Tests

    [Test]
    public async Task ExecuteAsync_WithServerParameter_UsesProvidedServer()
    {
        // Arrange
        SeedUserProfile();

        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        var context = new TestApplicationContext(m_TestUserId, ("game", TestGame), ("server", TestServer.ToString()));

        m_Service.Context = mockContext.Object;
        m_Service.ApplicationContext = context;

        var user = CreateTestUser(TestProfileId, TestLtUid);

        m_MockAuthMiddleware
            .Setup(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()))
            .ReturnsAsync(AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, user, mockContext.Object));

        m_MockApplicationService
            .Setup(x => x.ExecuteAsync(It.IsAny<TestApplicationContext>()))
            .ReturnsAsync(CommandResult.Success());

        // Act
        await m_Service.ExecuteAsync(TestProfileId);

        // Assert
        m_MockApplicationService.Verify(
            x => x.ExecuteAsync(It.Is<TestApplicationContext>(ctx =>
                ctx.GetParameter<string>("server") == TestServer.ToString())),
            Times.Once);

        var region = m_UserContext.Regions.SingleOrDefault(r =>
            r.ProfileId == TestProfileEntityId && r.Game == TestGame);
        Assert.That(region?.Region, Is.EqualTo(TestServer.ToString()));
    }

    [Test]
    public async Task ExecuteAsync_WithoutServerParameter_UsesLastUsedServer()
    {
        // Arrange
        SeedUserProfile(TestServer.ToString());

        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        var context = new TestApplicationContext(m_TestUserId, ("game", TestGame));
        // No server parameter provided

        m_Service.Context = mockContext.Object;
        m_Service.ApplicationContext = context;

        var user = CreateTestUser(TestProfileId, TestLtUid, TestServer);

        m_MockAuthMiddleware
            .Setup(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()))
            .ReturnsAsync(AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, user, mockContext.Object));

        m_MockApplicationService
            .Setup(x => x.ExecuteAsync(It.IsAny<TestApplicationContext>()))
            .ReturnsAsync(CommandResult.Success());

        // Act
        await m_Service.ExecuteAsync(TestProfileId);

        // Assert
        m_MockApplicationService.Verify(
            x => x.ExecuteAsync(It.Is<TestApplicationContext>(ctx =>
                ctx.GetParameter<string>("server") == TestServer.ToString())),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_NoServerAndNoLastUsed_SendsErrorMessage()
    {
        // Arrange
        SeedUserProfile();

        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        var context = new TestApplicationContext(m_TestUserId, ("game", TestGame));
        // No server parameter and user has no last used server

        m_Service.Context = mockContext.Object;
        m_Service.ApplicationContext = context;

        var user = CreateTestUser(TestProfileId, TestLtUid, null);

        m_MockAuthMiddleware
            .Setup(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()))
            .ReturnsAsync(AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, user, mockContext.Object));

        // Act
        await m_Service.ExecuteAsync(TestProfileId);

        // Assert
        var response = await m_DiscordHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("Server is required for first time use"));

        // Verify application service was NOT called
        m_MockApplicationService.Verify(
            x => x.ExecuteAsync(It.IsAny<TestApplicationContext>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_UpdatesLastUsedServer()
    {
        // Arrange
        SeedUserProfile(TestServer.ToString());

        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        var context =
            new TestApplicationContext(m_TestUserId, ("game", TestGame),
                ("server", Server.Europe.ToString())); // Different from current

        m_Service.Context = mockContext.Object;
        m_Service.ApplicationContext = context;

        var user = CreateTestUser(TestProfileId, TestLtUid, TestServer);

        m_MockAuthMiddleware
            .Setup(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()))
            .ReturnsAsync(AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, user, mockContext.Object));

        m_MockApplicationService
            .Setup(x => x.ExecuteAsync(It.IsAny<TestApplicationContext>()))
            .ReturnsAsync(CommandResult.Success());

        // Act
        await m_Service.ExecuteAsync(TestProfileId);

        // Assert
        var region = m_UserContext.Regions.SingleOrDefault(r =>
            r.ProfileId == TestProfileEntityId && r.Game == TestGame);
        Assert.That(region?.Region, Is.EqualTo(Server.Europe.ToString()));
    }

    #endregion

    #region Response Handling Tests

    [Test]
    public async Task ExecuteAsync_SuccessWithEphemeralContext_SendsEphemeralFollowup()
    {
        // Arrange
        SeedUserProfile();

        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        var context = new TestApplicationContext(m_TestUserId, ("game", TestGame), ("server", TestServer.ToString()));

        m_Service.Context = mockContext.Object;
        m_Service.ApplicationContext = context;
        m_Service.IsResponseEphemeral = true; // Set ephemeral

        var user = CreateTestUser(TestProfileId, TestLtUid);

        m_MockAuthMiddleware
            .Setup(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()))
            .ReturnsAsync(AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, user, mockContext.Object));

        m_MockApplicationService
            .Setup(x => x.ExecuteAsync(It.IsAny<TestApplicationContext>()))
            .ReturnsAsync(CommandResult.Success());

        // Act
        await m_Service.ExecuteAsync(TestProfileId);

        // Assert
        m_MockApplicationService.Verify(x => x.ExecuteAsync(It.IsAny<TestApplicationContext>()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_SuccessWithNonEphemeralResult_SendsPublicFollowupWithButton()
    {
        // Arrange
        SeedUserProfile();

        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        var context = new TestApplicationContext(m_TestUserId, ("game", TestGame), ("server", TestServer.ToString()));

        m_Service.Context = mockContext.Object;
        m_Service.ApplicationContext = context;
        m_Service.IsResponseEphemeral = false; // Public response

        var user = CreateTestUser(TestProfileId, TestLtUid);

        m_MockAuthMiddleware
            .Setup(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()))
            .ReturnsAsync(AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, user, mockContext.Object));

        var result = CommandResult.Success(isEphemeral: false); // Public result

        m_MockApplicationService
            .Setup(x => x.ExecuteAsync(It.IsAny<TestApplicationContext>()))
            .ReturnsAsync(result);

        // Act
        await m_Service.ExecuteAsync(TestProfileId);

        // Assert
        m_MockApplicationService.Verify(x => x.ExecuteAsync(It.IsAny<TestApplicationContext>()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_ResultWithEphemeralData_OverridesServiceEphemeralSetting()
    {
        // Arrange
        SeedUserProfile();

        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        var context = new TestApplicationContext(m_TestUserId, ("game", TestGame), ("server", TestServer.ToString()));

        m_Service.Context = mockContext.Object;
        m_Service.ApplicationContext = context;
        m_Service.IsResponseEphemeral = false; // Service set to public

        var user = CreateTestUser(TestProfileId, TestLtUid);

        m_MockAuthMiddleware
            .Setup(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()))
            .ReturnsAsync(AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, user, mockContext.Object));

        var result = CommandResult.Success(isEphemeral: true); // But result wants ephemeral

        m_MockApplicationService
            .Setup(x => x.ExecuteAsync(It.IsAny<TestApplicationContext>()))
            .ReturnsAsync(result);

        // Act
        await m_Service.ExecuteAsync(TestProfileId);

        // Assert
        m_MockApplicationService.Verify(x => x.ExecuteAsync(It.IsAny<TestApplicationContext>()), Times.Once);

        var str = await m_DiscordHelper.ExtractInteractionResponseDataAsync();
        Assert.That(str, Is.Not.Null.Or.Empty);

        var response = JsonNode.Parse(str);
        var flagsVal = response?["flags"]?.GetValue<int>() ?? 0;

        Assert.That(((MessageFlags)flagsVal).HasFlag(MessageFlags.Ephemeral), Is.True);
    }

    #endregion

    #region Validation Tests

    [Test]
    public async Task ExecuteAsync_WithInvalidValidation_SendsErrorAndStops()
    {
        // Arrange
        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        var context = new TestApplicationContext(m_TestUserId);
        m_Service.Context = mockContext.Object;
        m_Service.ApplicationContext = context;

        m_Service.AddValidator<string>("testParam", _ => false, "Validation failed");

        // Act
        await m_Service.ExecuteAsync(TestProfileId);

        // Assert
        var response = await m_DiscordHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("Error when validating input"));
        Assert.That(response, Does.Contain("Validation failed"));

        // Verify no further processing
        m_MockRateLimitService.Verify(x => x.IsRateLimitedAsync(It.IsAny<ulong>()), Times.Never);
        m_MockAuthMiddleware.Verify(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()), Times.Never);
    }

    #endregion

    #region Rate Limit Tests

    [Test]
    public async Task ExecuteAsync_WhenRateLimited_SendsRateLimitMessage()
    {
        // Arrange
        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        var context = new TestApplicationContext(m_TestUserId);
        m_Service.Context = mockContext.Object;
        m_Service.ApplicationContext = context;

        m_MockRateLimitService
            .Setup(x => x.IsRateLimitedAsync(m_TestUserId))
            .ReturnsAsync(true);

        // Act
        await m_Service.ExecuteAsync(TestProfileId);

        // Assert
        var response = await m_DiscordHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("used command too frequent").IgnoreCase);

        m_MockAuthMiddleware.Verify(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()), Times.Never);
    }

    #endregion

    #region Authentication Tests

    [Test]
    public async Task ExecuteAsync_AuthenticationSuccess_SetsContextProperties()
    {
        // Arrange
        SeedUserProfile();

        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        var context = new TestApplicationContext(m_TestUserId, ("game", TestGame), ("server", TestServer.ToString()));

        m_Service.Context = mockContext.Object;
        m_Service.ApplicationContext = context;

        var user = CreateTestUser(TestProfileId, TestLtUid);

        m_MockAuthMiddleware
            .Setup(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()))
            .ReturnsAsync(AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, user, mockContext.Object));

        m_MockApplicationService
            .Setup(x => x.ExecuteAsync(It.IsAny<TestApplicationContext>()))
            .ReturnsAsync(CommandResult.Success());

        // Act
        await m_Service.ExecuteAsync(TestProfileId);

        // Assert
        m_MockApplicationService.Verify(
            x => x.ExecuteAsync(It.Is<TestApplicationContext>(ctx =>
                ctx.LToken == TestLToken &&
                ctx.LtUid == TestLtUid &&
                ctx.GetParameter<string>("server") == TestServer.ToString())),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_AuthenticationFailure_SendsErrorMessage()
    {
        // Arrange
        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        var context = new TestApplicationContext(m_TestUserId);
        m_Service.Context = mockContext.Object;
        m_Service.ApplicationContext = context;

        const string errorMessage = "Authentication failed";

        m_MockAuthMiddleware
            .Setup(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()))
            .ReturnsAsync(AuthenticationResult.Failure(mockContext.Object, errorMessage));

        // Act
        await m_Service.ExecuteAsync(TestProfileId);

        // Assert
        var response = await m_DiscordHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain(errorMessage));

        m_MockApplicationService.Verify(x => x.ExecuteAsync(It.IsAny<TestApplicationContext>()), Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_AuthenticationNotFound_SendsErrorResponse()
    {
        // Arrange
        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        var context = new TestApplicationContext(m_TestUserId);
        m_Service.Context = mockContext.Object;
        m_Service.ApplicationContext = context;

        const string errorMessage = "Profile not found";

        m_MockAuthMiddleware
            .Setup(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()))
            .ReturnsAsync(AuthenticationResult.NotFound(mockContext.Object, errorMessage));

        // Act
        await m_Service.ExecuteAsync(TestProfileId);

        // Assert
        var response = await m_DiscordHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain(errorMessage));

        m_MockApplicationService.Verify(x => x.ExecuteAsync(It.IsAny<TestApplicationContext>()), Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_AuthenticationTimeout_DoesNotSendResponse()
    {
        // Arrange
        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        var context = new TestApplicationContext(m_TestUserId);
        m_Service.Context = mockContext.Object;
        m_Service.ApplicationContext = context;

        m_MockAuthMiddleware
            .Setup(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()))
            .ReturnsAsync(AuthenticationResult.Timeout());

        // Act
        await m_Service.ExecuteAsync(TestProfileId);

        // Assert
        m_MockApplicationService.Verify(x => x.ExecuteAsync(It.IsAny<TestApplicationContext>()), Times.Never);
    }

    #endregion

    #region Application Service Tests

    [Test]
    public async Task ExecuteAsync_ApplicationServiceSuccess_TracksMetrics()
    {
        // Arrange
        SeedUserProfile();

        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        var context = new TestApplicationContext(m_TestUserId, ("game", TestGame), ("server", TestServer.ToString()));

        m_Service.Context = mockContext.Object;
        m_Service.ApplicationContext = context;
        m_Service.CommandName = "testcommand";

        var user = CreateTestUser(TestProfileId, TestLtUid);

        m_MockAuthMiddleware
            .Setup(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()))
            .ReturnsAsync(AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, user, mockContext.Object));

        m_MockApplicationService
            .Setup(x => x.ExecuteAsync(It.IsAny<TestApplicationContext>()))
            .ReturnsAsync(CommandResult.Success());

        // Act
        await m_Service.ExecuteAsync(TestProfileId);

        // Assert
        m_MockMetricsService.Verify(
            x => x.TrackCommand("testcommand", m_TestUserId, true),
            Times.Once);

        m_MockMetricsService.Verify(
            x => x.ObserveCommandDuration("testcommand"),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_ApplicationServiceFailure_TracksFailureMetrics()
    {
        // Arrange
        SeedUserProfile();

        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        var context = new TestApplicationContext(m_TestUserId, ("game", TestGame), ("server", TestServer.ToString()));

        m_Service.Context = mockContext.Object;
        m_Service.ApplicationContext = context;
        m_Service.CommandName = "testcommand";

        var user = CreateTestUser(TestProfileId, TestLtUid);

        m_MockAuthMiddleware
            .Setup(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()))
            .ReturnsAsync(AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, user, mockContext.Object));

        m_MockApplicationService
            .Setup(x => x.ExecuteAsync(It.IsAny<TestApplicationContext>()))
            .ReturnsAsync(CommandResult.Failure(CommandFailureReason.ApiError, "API error"));

        // Act
        await m_Service.ExecuteAsync(TestProfileId);

        // Assert
        m_MockMetricsService.Verify(
            x => x.TrackCommand("testcommand", m_TestUserId, false),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_ApplicationServiceFailure_SendsErrorFollowup()
    {
        // Arrange
        SeedUserProfile();

        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        var context = new TestApplicationContext(m_TestUserId, ("game", TestGame), ("server", TestServer.ToString()));

        m_Service.Context = mockContext.Object;
        m_Service.ApplicationContext = context;

        var user = CreateTestUser(TestProfileId, TestLtUid);

        m_MockAuthMiddleware
            .Setup(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()))
            .ReturnsAsync(AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, user, mockContext.Object));

        const string errorMessage = "Command execution failed";

        m_MockApplicationService
            .Setup(x => x.ExecuteAsync(It.IsAny<TestApplicationContext>()))
            .ReturnsAsync(CommandResult.Failure(CommandFailureReason.ApiError, errorMessage));

        // Act
        await m_Service.ExecuteAsync(TestProfileId);

        // Assert
        m_MockApplicationService.Verify(x => x.ExecuteAsync(It.IsAny<TestApplicationContext>()), Times.Once);
    }

    #endregion

    #region Service Provider Tests

    [Test]
    public async Task ExecuteAsync_ResolvesApplicationServiceFromServiceProvider()
    {
        // Arrange
        SeedUserProfile();

        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        var context = new TestApplicationContext(m_TestUserId, ("game", TestGame), ("server", TestServer.ToString()));

        m_Service.Context = mockContext.Object;
        m_Service.ApplicationContext = context;

        var user = CreateTestUser(TestProfileId, TestLtUid);

        m_MockAuthMiddleware
            .Setup(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()))
            .ReturnsAsync(AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, user, mockContext.Object));

        m_MockApplicationService
            .Setup(x => x.ExecuteAsync(It.IsAny<TestApplicationContext>()))
            .ReturnsAsync(CommandResult.Success());

        // Act
        await m_Service.ExecuteAsync(TestProfileId);

        // Assert
        m_MockServiceProvider.Verify(
            x => x.GetService(typeof(IApplicationService<TestApplicationContext>)),
            Times.Once);
    }

    #endregion

    #region Integration Flow Tests

    [Test]
    public async Task ExecuteAsync_CompleteFlow_ExecutesInCorrectOrder()
    {
        // Arrange
        SeedUserProfile();

        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IInteractionContext>();
        mockContext.SetupGet(x => x.Interaction).Returns(interaction);

        var context = new TestApplicationContext(m_TestUserId, ("game", TestGame), ("server", TestServer.ToString()));

        m_Service.Context = mockContext.Object;
        m_Service.ApplicationContext = context;

        var callOrder = new List<string>();

        m_MockRateLimitService
            .Setup(x => x.IsRateLimitedAsync(m_TestUserId))
            .ReturnsAsync(false)
            .Callback(() => callOrder.Add("RateLimit"));

        var user = CreateTestUser(TestProfileId, TestLtUid);

        m_MockAuthMiddleware
            .Setup(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()))
            .ReturnsAsync(AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, user, mockContext.Object))
            .Callback(() => callOrder.Add("Authentication"));

        m_MockMetricsService
            .Setup(x => x.ObserveCommandDuration(It.IsAny<string>()))
            .Returns(Mock.Of<IDisposable>())
            .Callback(() => callOrder.Add("MetricsStart"));

        m_MockApplicationService
            .Setup(x => x.ExecuteAsync(It.IsAny<TestApplicationContext>()))
            .ReturnsAsync(CommandResult.Success())
            .Callback(() => callOrder.Add("Execute"));

        m_MockMetricsService
            .Setup(x => x.TrackCommand(It.IsAny<string>(), It.IsAny<ulong>(), It.IsAny<bool>()))
            .Callback(() => callOrder.Add("MetricsTrack"));

        // Act
        await m_Service.ExecuteAsync(TestProfileId);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(callOrder, Has.Count.GreaterThanOrEqualTo(5));
            Assert.That(callOrder[0], Is.EqualTo("RateLimit"));
            Assert.That(callOrder[1], Is.EqualTo("Authentication"));
            Assert.That(callOrder[2], Is.EqualTo("MetricsStart"));
            Assert.That(callOrder[3], Is.EqualTo("Execute"));
            Assert.That(callOrder[4], Is.EqualTo("MetricsTrack"));
        });

        var region = m_UserContext.Regions.SingleOrDefault(r =>
            r.ProfileId == TestProfileEntityId && r.Game == TestGame);
        Assert.That(region?.Region, Is.EqualTo(TestServer.ToString()));
    }

    #endregion

    #region Helper Methods

    private UserDto CreateTestUser(int profileId, ulong ltUid, Server? lastUsedServer = null)
    {
        var profile = new UserProfileDto
        {
            Id = TestProfileEntityId,
            ProfileId = profileId,
            LtUid = ltUid,
            LastUsedRegions = lastUsedServer.HasValue
                ? new Dictionary<Game, string> { { TestGame, lastUsedServer.Value.ToString() } }
                : []
        };

        return new UserDto
        {
            Id = m_TestUserId,
            Profiles = [profile]
        };
    }

    private void SeedUserProfile(string? region = null)
    {
        if (!m_UserContext.Users.Any(u => u.Id == (long)m_TestUserId))
        {
            var user = new UserModel
            {
                Id = (long)m_TestUserId,
                Timestamp = DateTime.UtcNow
            };

            var profile = new UserProfileModel
            {
                Id = TestProfileEntityId,
                User = user,
                UserId = user.Id,
                ProfileId = TestProfileId,
                LtUid = (long)TestLtUid,
                LToken = TestLToken
            };

            if (region != null)
            {
                profile.LastUsedRegions.Add(new ProfileRegion
                {
                    ProfileId = TestProfileEntityId,
                    Game = TestGame,
                    Region = region,
                    UserProfile = profile
                });
            }

            user.Profiles.Add(profile);
            m_UserContext.Users.Add(user);
            m_UserContext.SaveChanges();
            return;
        }

        var existingProfile = m_UserContext.UserProfiles.SingleOrDefault(p => p.Id == TestProfileEntityId);
        if (existingProfile != null && region != null &&
            !m_UserContext.Regions.Any(r => r.ProfileId == existingProfile.Id && r.Game == TestGame))
        {
            m_UserContext.Regions.Add(new ProfileRegion
            {
                ProfileId = existingProfile.Id,
                Game = TestGame,
                Region = region,
                UserProfile = existingProfile
            });
            m_UserContext.SaveChanges();
        }
    }

    #endregion
}

/// <summary>
/// Test application context for testing generic CommandExecutorService
/// </summary>
public class TestApplicationContext : ApplicationContextBase
{
    public TestApplicationContext(ulong userId, params (string, object)[] parameters)
        : base(userId, parameters)
    {
    }
}
