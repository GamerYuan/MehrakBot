#region

using System.Text;
using MehrakCore.Tests.TestHelpers;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

#endregion

namespace MehrakCore.Tests.Services.Commands.Common;

[Parallelizable(ParallelScope.Fixtures)]
public class DailyCheckInCommandExecutorTests
{
    private DiscordTestHelper m_DiscordTestHelper;
    private DailyCheckInCommandExecutor m_Executor;
    private Mock<IDailyCheckInService> m_DailyCheckInServiceMock;
    private UserRepository m_UserRepository;
    private Mock<IDistributedCache> m_DistributedCacheMock;
    private RedisCacheService m_TokenCacheService;
    private Mock<IAuthenticationMiddlewareService> m_AuthenticationMiddlewareMock;
    private Mock<ILogger<DailyCheckInCommandExecutor>> m_LoggerMock;
    private ServiceProvider m_ServiceProvider;
    private ApplicationCommandService<ApplicationCommandContext> m_CommandService;

    private ulong m_TestUserId;
    private const ulong TestLtUid = 987654321UL;
    private const string TestLToken = "test_ltoken_value";
    private const string TestGuid = "test-guid-12345";

    [SetUp]
    public async Task Setup()
    {
        // Setup Discord helper with daily check-in command
        JsonApplicationCommand commandJson = new()
        {
            Id = 123456789UL,
            Name = "dailycheckin",
            Description = "Perform daily check-in",
            Type = ApplicationCommandType.ChatInput
        };
        m_DiscordTestHelper = new DiscordTestHelper(commandJson);

        m_CommandService = new ApplicationCommandService<ApplicationCommandContext>();
        await m_CommandService.RegisterCommandsAsync(m_DiscordTestHelper.DiscordClient.Rest, 123456789UL);

        // Setup mocks and real services
        m_DailyCheckInServiceMock = new Mock<IDailyCheckInService>();
        m_DistributedCacheMock = new Mock<IDistributedCache>();
        m_AuthenticationMiddlewareMock = new Mock<IAuthenticationMiddlewareService>();
        m_LoggerMock = new Mock<ILogger<DailyCheckInCommandExecutor>>();

        // Create real instances
        m_UserRepository =
            new UserRepository(MongoTestHelper.Instance.MongoDbService, NullLogger<UserRepository>.Instance);
        m_TokenCacheService =
            new RedisCacheService(m_DistributedCacheMock.Object, NullLogger<RedisCacheService>.Instance);

        GameRecordApiService gameRecordApiService = new(
            Mock.Of<IHttpClientFactory>(),
            NullLogger<GameRecordApiService>.Instance);

        m_TestUserId = MongoTestHelper.Instance.GetUniqueUserId();

        m_Executor = new DailyCheckInCommandExecutor(
            m_DailyCheckInServiceMock.Object,
            m_UserRepository,
            m_TokenCacheService,
            m_AuthenticationMiddlewareMock.Object,
            gameRecordApiService,
            m_LoggerMock.Object);

        // Setup service provider for command execution
        ServiceCollection services = new();
        services.AddSingleton(m_Executor);
        m_ServiceProvider = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        m_DiscordTestHelper.Dispose();
        m_ServiceProvider.Dispose();
    }

    private async Task ExecuteDailyCheckInCommand(ulong userId, uint? profile = null)
    {
        // Create parameters for the command
        List<(string, object, ApplicationCommandOptionType)> parameters = [];

        if (profile.HasValue)
            parameters.Add(("profile", profile.Value, ApplicationCommandOptionType.Integer));

        // Create interaction
        SlashCommandInteraction interaction = m_DiscordTestHelper.CreateCommandInteraction(
            userId,
            null,
            [.. parameters]
        );

        // Set the context for the executor
        ApplicationCommandContext context = new(interaction, m_DiscordTestHelper.DiscordClient);
        m_Executor.Context = context;

        // Execute directly on the executor
        await m_Executor.ExecuteAsync(profile);
    }

    [Test]
    public void ExecuteAsync_WithInvalidParametersCount_ShouldThrowArgumentException()
    {
        // Act & Assert
        Assert.ThrowsAsync<ArgumentException>(async () => await m_Executor.ExecuteAsync());
        Assert.ThrowsAsync<ArgumentException>(async () => await m_Executor.ExecuteAsync(1u, 2u));
    }

    [Test]
    public async Task ExecuteAsync_WithNullProfile_ShouldUseDefaultProfile()
    {
        // Arrange
        UserModel user = new() { Id = m_TestUserId, Profiles = [] };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Act
        await ExecuteDailyCheckInCommand(m_TestUserId);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("You do not have a profile"));
    }

    [Test]
    public async Task ExecuteAsync_WithUserHavingNoProfiles_ShouldReturnNoProfileMessage()
    {
        // Arrange
        UserModel user = new() { Id = m_TestUserId, Profiles = [] };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Act
        await ExecuteDailyCheckInCommand(m_TestUserId, 1u);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("You do not have a profile"));
    }

    [Test]
    public async Task ExecuteAsync_WithUserHavingNullProfiles_ShouldReturnNoProfileMessage()
    {
        // Arrange
        UserModel user = new() { Id = m_TestUserId, Profiles = null };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Act
        await ExecuteDailyCheckInCommand(m_TestUserId, 1u);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("You do not have a profile"));
    }

    [Test]
    public async Task ExecuteAsync_WithNonExistentProfile_ShouldReturnNoProfileMessage()
    {
        // Arrange
        UserModel user = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new UserProfile { ProfileId = 2, LtUid = TestLtUid }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Act
        await ExecuteDailyCheckInCommand(m_TestUserId, 1u); // Profile 1 doesn't exist

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("You do not have a profile"));
    }

    [Test]
    public async Task ExecuteAsync_WithNonExistentUser_ShouldReturnNoProfileMessage()
    {
        // Act
        await ExecuteDailyCheckInCommand(m_TestUserId + 1, 1u);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("You do not have a profile"));
    }

    [Test]
    public async Task ExecuteAsync_WithValidProfileButNoToken_ShouldTriggerAuthentication()
    {
        // Arrange
        UserModel user = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new UserProfile { ProfileId = 1, LtUid = TestLtUid }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user); // Setup token cache to return null (no cached token)
        m_DistributedCacheMock.Setup(x =>
                x.GetAsync($"TokenCache_{m_TestUserId}_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        m_AuthenticationMiddlewareMock.Setup(x =>
                x.RegisterAuthenticationListener(m_TestUserId, It.IsAny<IAuthenticationListener>()))
            .Returns(TestGuid);

        // Act
        await ExecuteDailyCheckInCommand(m_TestUserId, 1u); // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        // Should receive an authentication modal with the expected structure
        Assert.That(response, Contains.Substring("auth_modal:test-guid-12345:1"));
        Assert.That(response, Contains.Substring("Authenticate"));
        Assert.That(response, Contains.Substring("passphrase"));
        m_AuthenticationMiddlewareMock.Verify(
            x => x.RegisterAuthenticationListener(m_TestUserId, It.IsAny<IAuthenticationListener>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_WithValidProfileAndToken_ShouldCallCheckInService()
    {
        // Arrange
        UserModel user = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new UserProfile { ProfileId = 1, LtUid = TestLtUid, LastCheckIn = null }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Setup token cache to return valid token
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x =>
                x.GetAsync($"TokenCache_{m_TestUserId}_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);

        // Act
        await ExecuteDailyCheckInCommand(m_TestUserId, 1u); // Assert
        m_DailyCheckInServiceMock.Verify(
            x => x.CheckInAsync(TestLtUid, TestLToken),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_WhenAlreadyCheckedInToday_ShouldReturnEarlyTerminationMessage()
    {
        // Arrange
        TimeZoneInfo chinaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        DateTime nowUtc8 = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, chinaTimeZone);
        DateTime lastCheckInUtc =
            TimeZoneInfo.ConvertTimeToUtc(nowUtc8.Date.AddHours(10), chinaTimeZone); // Same day in UTC+8

        UserModel user = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new UserProfile { ProfileId = 1, LtUid = TestLtUid, LastCheckIn = lastCheckInUtc }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Setup token cache to return valid token (shouldn't be used)
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x =>
                x.GetAsync($"TokenCache_{m_TestUserId}_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);

        // Act
        await ExecuteDailyCheckInCommand(m_TestUserId, 1u);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("You have already checked in today for this profile"));

        // Verify that the check-in service was NOT called
        m_DailyCheckInServiceMock.Verify(
            x => x.CheckInAsync(It.IsAny<ulong>(), It.IsAny<string>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_WhenCheckedInYesterday_ShouldProceedWithCheckIn()
    {
        // Arrange
        TimeZoneInfo chinaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        DateTime nowUtc8 = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, chinaTimeZone);
        DateTime yesterdayUtc8 = nowUtc8.Date.AddDays(-1).AddHours(10);
        DateTime lastCheckInUtc = TimeZoneInfo.ConvertTimeToUtc(yesterdayUtc8, chinaTimeZone);

        UserModel user = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new UserProfile { ProfileId = 1, LtUid = TestLtUid, LastCheckIn = lastCheckInUtc }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Setup token cache to return valid token
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x =>
                x.GetAsync($"TokenCache_{m_TestUserId}_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);

        // Act
        await ExecuteDailyCheckInCommand(m_TestUserId, 1u);

        // Assert
        m_DailyCheckInServiceMock.Verify(
            x => x.CheckInAsync(TestLtUid, TestLToken),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_WhenCrossedMidnightInChinaTimeZone_ShouldProceedWithCheckIn()
    {
        // Arrange - Simulate checking in late at night and then early next
        // morning in China time
        TimeZoneInfo chinaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");

        // Previous check-in was at 23:30 China time yesterday
        DateTime yesterdayUtc8 = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, chinaTimeZone).Date.AddDays(-1)
            .AddHours(23).AddMinutes(30);
        DateTime lastCheckInUtc = TimeZoneInfo.ConvertTimeToUtc(yesterdayUtc8, chinaTimeZone);

        UserModel user = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new UserProfile { ProfileId = 1, LtUid = TestLtUid, LastCheckIn = lastCheckInUtc }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Setup token cache to return valid token
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x =>
                x.GetAsync($"TokenCache_{m_TestUserId}_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);

        // Act
        await ExecuteDailyCheckInCommand(m_TestUserId, 1u);

        // Assert
        m_DailyCheckInServiceMock.Verify(
            x => x.CheckInAsync(TestLtUid, TestLToken),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_WithNullLastCheckIn_ShouldProceedWithCheckIn()
    {
        // Arrange
        UserModel user = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new UserProfile { ProfileId = 1, LtUid = TestLtUid, LastCheckIn = null }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Setup token cache to return valid token
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x =>
                x.GetAsync($"TokenCache_{m_TestUserId}_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);

        // Act
        await ExecuteDailyCheckInCommand(m_TestUserId, 1u);

        // Assert
        m_DailyCheckInServiceMock.Verify(
            x => x.CheckInAsync(TestLtUid, TestLToken),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_WhenExceptionThrown_ShouldSendErrorMessage()
    {
        // Arrange - Create a scenario that will throw an exception
        UserModel user = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new UserProfile { ProfileId = 1, LtUid = TestLtUid }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x =>
                x.GetAsync($"TokenCache_{m_TestUserId}_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes); // Make CheckInAsync throw an exception
        m_DailyCheckInServiceMock.Setup(x =>
                x.CheckInAsync(It.IsAny<ulong>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("Test exception"));

        // Act
        await ExecuteDailyCheckInCommand(m_TestUserId, 1u);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("An unknown error occurred"));
    }

    [Test]
    public async Task OnAuthenticationCompletedAsync_AuthenticationFailed_LogsError()
    {
        // Arrange
        AuthenticationResult result = AuthenticationResult.Failure(m_TestUserId, "Authentication failed");

        // Act
        await m_Executor.OnAuthenticationCompletedAsync(result);

        // Assert
        m_LoggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Authentication failed")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public async Task OnAuthenticationCompletedAsync_WithSuccessfulResult_ShouldCallCheckInService()
    {
        // Arrange Create interaction
        (string Name, object Value, ApplicationCommandOptionType Type)[] parameters = [];
        SlashCommandInteraction interaction = m_DiscordTestHelper.CreateCommandInteraction(
            m_TestUserId,
            null,
            parameters
        );

        // Set the context for the executor
        ApplicationCommandContext context = new(interaction, m_DiscordTestHelper.DiscordClient);
        AuthenticationResult result = AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, context);

        // Set up pending profile by calling ExecuteAsync first
        UserModel user = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new UserProfile { ProfileId = 1, LtUid = TestLtUid }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);
        m_DistributedCacheMock.Setup(x =>
                x.GetAsync($"TokenCache_{m_TestUserId}_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
        m_AuthenticationMiddlewareMock.Setup(x =>
                x.RegisterAuthenticationListener(m_TestUserId, It.IsAny<IAuthenticationListener>()))
            .Returns(TestGuid);

        await ExecuteDailyCheckInCommand(m_TestUserId, 1u); // This sets m_PendingProfile

        // Act
        await m_Executor.OnAuthenticationCompletedAsync(result);

        // Assert
        m_DailyCheckInServiceMock.Verify(
            x => x.CheckInAsync(TestLtUid, TestLToken),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_WithMultipleProfiles_ShouldSelectCorrectProfile()
    {
        // Arrange
        UserModel user = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new UserProfile { ProfileId = 1, LtUid = TestLtUid },
                new UserProfile { ProfileId = 2, LtUid = TestLtUid + 1 },
                new UserProfile { ProfileId = 3, LtUid = TestLtUid + 2 }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Setup token cache for profile 2
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x =>
                x.GetAsync($"TokenCache_{m_TestUserId}_{TestLtUid + 1}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);

        // Act
        await ExecuteDailyCheckInCommand(m_TestUserId, 2u); // Select profile 2        // Assert
        m_DailyCheckInServiceMock.Verify(
            x => x.CheckInAsync(TestLtUid + 1, TestLToken), Times.Once);
    }

    [Test]
    public void Context_ShouldBeSettable()
    {
        // Arrange
        SlashCommandInteraction interaction = m_DiscordTestHelper.CreateCommandInteraction(m_TestUserId);
        ApplicationCommandContext newContext = new(interaction, m_DiscordTestHelper.DiscordClient);

        // Act
        m_Executor.Context = newContext;

        // Assert
        Assert.That(m_Executor.Context, Is.EqualTo(newContext));
    }

    [Test]
    public async Task ExecuteAsync_WhenCheckedInJustBeforeMidnightUtc8_ShouldAllowNextDayCheckIn()
    {
        // Arrange - Simulate checking in at 23:58 UTC+8 yesterday and now it's
        // 00:01 UTC+8 today
        TimeZoneInfo chinaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        DateTime nowUtc8 = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, chinaTimeZone);

        // Set last check-in to 23:58 yesterday in UTC+8
        DateTime yesterdayLateUtc8 = nowUtc8.Date.AddDays(-1).AddHours(23).AddMinutes(58);
        DateTime lastCheckInUtc = TimeZoneInfo.ConvertTimeToUtc(yesterdayLateUtc8, chinaTimeZone);

        UserModel user = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new UserProfile { ProfileId = 1, LtUid = TestLtUid, LastCheckIn = lastCheckInUtc }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Setup token cache to return valid token
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x =>
                x.GetAsync($"TokenCache_{m_TestUserId}_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);

        // Act
        await ExecuteDailyCheckInCommand(m_TestUserId, 1u);

        // Assert
        m_DailyCheckInServiceMock.Verify(
            x => x.CheckInAsync(TestLtUid, TestLToken),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_WhenCheckedInEarlyMorningUtc8SameDay_ShouldPreventSecondCheckIn()
    {
        // Arrange - Simulate checking in at 01:00 UTC+8 today and now it's
        // 23:00 UTC+8 same day
        TimeZoneInfo chinaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        DateTime nowUtc8 = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, chinaTimeZone);

        // Set last check-in to early morning today in UTC+8
        DateTime earlyTodayUtc8 = nowUtc8.Date.AddHours(1);
        DateTime lastCheckInUtc = TimeZoneInfo.ConvertTimeToUtc(earlyTodayUtc8, chinaTimeZone);

        UserModel user = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new UserProfile { ProfileId = 1, LtUid = TestLtUid, LastCheckIn = lastCheckInUtc }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Setup token cache to return valid token (shouldn't be used)
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x =>
                x.GetAsync($"TokenCache_{m_TestUserId}_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);

        // Act
        await ExecuteDailyCheckInCommand(m_TestUserId, 1u);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("You have already checked in today for this profile"));

        // Verify that the check-in service was NOT called
        m_DailyCheckInServiceMock.Verify(
            x => x.CheckInAsync(It.IsAny<ulong>(), It.IsAny<string>()),
            Times.Never);
    }
}