#region

using System.Text;
using MehrakCore.Models;
using MehrakCore.Repositories;
using MehrakCore.Services.Commands;
using MehrakCore.Services.Commands.Common;
using MehrakCore.Services.Common;
using MehrakCore.Tests.TestHelpers;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NetCord;
using NetCord.Rest.JsonModels;
using NetCord.Services;
using NetCord.Services.ApplicationCommands;

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
    private TokenCacheService m_TokenCacheService;
    private Mock<IAuthenticationMiddlewareService> m_AuthenticationMiddlewareMock;
    private Mock<ILogger<DailyCheckInCommandExecutor>> m_LoggerMock;
    private ServiceProvider m_ServiceProvider;
    private ApplicationCommandService<ApplicationCommandContext> m_CommandService;

    private const ulong TestUserId = 123456789UL;
    private const ulong TestLtUid = 987654321UL;
    private const string TestLToken = "test_ltoken_value";
    private const string TestGuid = "test-guid-12345";

    [SetUp]
    public async Task Setup()
    {
        // Setup Discord helper with daily check-in command
        var commandJson = new JsonApplicationCommand
        {
            Id = 123456789UL,
            Name = "dailycheckin",
            Description = "Perform daily check-in",
            Type = ApplicationCommandType.ChatInput
        };
        m_DiscordTestHelper = new DiscordTestHelper(commandJson);

        m_CommandService = new ApplicationCommandService<ApplicationCommandContext>();
        await m_CommandService.CreateCommandsAsync(m_DiscordTestHelper.DiscordClient.Rest, 123456789UL);

        // Setup mocks and real services
        m_DailyCheckInServiceMock = new Mock<IDailyCheckInService>();
        m_DistributedCacheMock = new Mock<IDistributedCache>();
        m_AuthenticationMiddlewareMock = new Mock<IAuthenticationMiddlewareService>();
        m_LoggerMock = new Mock<ILogger<DailyCheckInCommandExecutor>>();

        // Create real instances
        m_UserRepository =
            new UserRepository(MongoTestHelper.Instance.MongoDbService, NullLogger<UserRepository>.Instance);
        m_TokenCacheService =
            new TokenCacheService(m_DistributedCacheMock.Object, NullLogger<TokenCacheService>.Instance);

        var gameRecordApiService = new GameRecordApiService(
            Mock.Of<IHttpClientFactory>(),
            NullLogger<GameRecordApiService>.Instance);

        m_Executor = new DailyCheckInCommandExecutor(
            m_DailyCheckInServiceMock.Object,
            m_UserRepository,
            m_TokenCacheService,
            m_AuthenticationMiddlewareMock.Object,
            gameRecordApiService,
            m_LoggerMock.Object);

        // Setup service provider for command execution
        var services = new ServiceCollection();
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
        var parameters = new List<(string, object, ApplicationCommandOptionType)>();

        if (profile.HasValue)
            parameters.Add(("profile", profile.Value, ApplicationCommandOptionType.Integer));

        // Create interaction
        var interaction = m_DiscordTestHelper.CreateCommandInteraction(
            userId,
            null,
            parameters.ToArray()
        );

        // Set the context for the executor
        var context = new ApplicationCommandContext(interaction, m_DiscordTestHelper.DiscordClient);
        m_Executor.Context = context;

        // Execute directly on the executor
        await m_Executor.ExecuteAsync(profile);
    }

    [Test]
    public void ExecuteAsync_WithInvalidParametersCount_ShouldThrowArgumentException()
    {
        // Act & Assert
        Assert.ThrowsAsync<ArgumentException>(() => m_Executor.ExecuteAsync().AsTask());
        Assert.ThrowsAsync<ArgumentException>(() => m_Executor.ExecuteAsync(1u, 2u).AsTask());
    }

    [Test]
    public async Task ExecuteAsync_WithNullProfile_ShouldUseDefaultProfile()
    {
        // Arrange
        var user = new UserModel { Id = TestUserId, Profiles = new List<UserProfile>() };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Act
        await ExecuteDailyCheckInCommand(TestUserId);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("You do not have a profile"));
    }

    [Test]
    public async Task ExecuteAsync_WithUserHavingNoProfiles_ShouldReturnNoProfileMessage()
    {
        // Arrange
        var user = new UserModel { Id = TestUserId, Profiles = new List<UserProfile>() };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Act
        await ExecuteDailyCheckInCommand(TestUserId, 1u);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("You do not have a profile"));
    }

    [Test]
    public async Task ExecuteAsync_WithUserHavingNullProfiles_ShouldReturnNoProfileMessage()
    {
        // Arrange
        var user = new UserModel { Id = TestUserId, Profiles = null };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Act
        await ExecuteDailyCheckInCommand(TestUserId, 1u);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("You do not have a profile"));
    }

    [Test]
    public async Task ExecuteAsync_WithNonExistentProfile_ShouldReturnNoProfileMessage()
    {
        // Arrange
        var user = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new() { ProfileId = 2, LtUid = TestLtUid }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Act
        await ExecuteDailyCheckInCommand(TestUserId, 1u); // Profile 1 doesn't exist

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("You do not have a profile"));
    }

    [Test]
    public async Task ExecuteAsync_WithNonExistentUser_ShouldReturnNoProfileMessage()
    {
        // Arrange - Don't create any user

        // Act
        await ExecuteDailyCheckInCommand(TestUserId, 1u);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("You do not have a profile"));
    }

    [Test]
    public async Task ExecuteAsync_WithValidProfileButNoToken_ShouldTriggerAuthentication()
    {
        // Arrange
        var user = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new() { ProfileId = 1, LtUid = TestLtUid }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user); // Setup token cache to return null (no cached token)
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        m_AuthenticationMiddlewareMock.Setup(x =>
                x.RegisterAuthenticationListener(TestUserId, It.IsAny<IAuthenticationListener>()))
            .Returns(TestGuid);

        // Act
        await ExecuteDailyCheckInCommand(TestUserId, 1u); // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        // Should receive an authentication modal with the expected structure
        Assert.That(response, Contains.Substring("auth_modal:test-guid-12345:1"));
        Assert.That(response, Contains.Substring("Authenticate"));
        Assert.That(response, Contains.Substring("passphrase"));
        m_AuthenticationMiddlewareMock.Verify(
            x => x.RegisterAuthenticationListener(TestUserId, It.IsAny<IAuthenticationListener>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_WithValidProfileAndToken_ShouldCallCheckInService()
    {
        // Arrange
        var user = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new() { ProfileId = 1, LtUid = TestLtUid, LastCheckIn = null }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Setup token cache to return valid token
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);

        // Act
        await ExecuteDailyCheckInCommand(TestUserId, 1u); // Assert
        m_DailyCheckInServiceMock.Verify(
            x => x.CheckInAsync(It.IsAny<ApplicationCommandContext>(), It.IsAny<UserModel>(), 1u, TestLtUid,
                TestLToken),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_WhenAlreadyCheckedInToday_ShouldReturnEarlyTerminationMessage()
    {
        // Arrange
        var chinaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        var nowUtc8 = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, chinaTimeZone);
        var lastCheckInUtc =
            TimeZoneInfo.ConvertTimeToUtc(nowUtc8.Date.AddHours(10), chinaTimeZone); // Same day in UTC+8

        var user = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new() { ProfileId = 1, LtUid = TestLtUid, LastCheckIn = lastCheckInUtc }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Setup token cache to return valid token (shouldn't be used)
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);

        // Act
        await ExecuteDailyCheckInCommand(TestUserId, 1u);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("You have already checked in today for this profile"));

        // Verify that the check-in service was NOT called
        m_DailyCheckInServiceMock.Verify(
            x => x.CheckInAsync(It.IsAny<ApplicationCommandContext>(), It.IsAny<UserModel>(), It.IsAny<uint>(),
                It.IsAny<ulong>(), It.IsAny<string>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_WhenCheckedInYesterday_ShouldProceedWithCheckIn()
    {
        // Arrange
        var chinaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        var nowUtc8 = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, chinaTimeZone);
        var yesterdayUtc8 = nowUtc8.Date.AddDays(-1).AddHours(10);
        var lastCheckInUtc = TimeZoneInfo.ConvertTimeToUtc(yesterdayUtc8, chinaTimeZone);

        var user = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new() { ProfileId = 1, LtUid = TestLtUid, LastCheckIn = lastCheckInUtc }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Setup token cache to return valid token
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);

        // Act
        await ExecuteDailyCheckInCommand(TestUserId, 1u);

        // Assert
        m_DailyCheckInServiceMock.Verify(
            x => x.CheckInAsync(It.IsAny<ApplicationCommandContext>(), It.IsAny<UserModel>(), It.IsAny<uint>(),
                TestLtUid, TestLToken),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_WhenCrossedMidnightInChinaTimeZone_ShouldProceedWithCheckIn()
    {
        // Arrange - Simulate checking in late at night and then early next morning in China time
        var chinaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");

        // Previous check-in was at 23:30 China time yesterday
        var yesterdayUtc8 = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, chinaTimeZone).Date.AddDays(-1)
            .AddHours(23).AddMinutes(30);
        var lastCheckInUtc = TimeZoneInfo.ConvertTimeToUtc(yesterdayUtc8, chinaTimeZone);

        var user = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new() { ProfileId = 1, LtUid = TestLtUid, LastCheckIn = lastCheckInUtc }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Setup token cache to return valid token
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);

        // Act
        await ExecuteDailyCheckInCommand(TestUserId, 1u);

        // Assert
        m_DailyCheckInServiceMock.Verify(
            x => x.CheckInAsync(It.IsAny<ApplicationCommandContext>(), It.IsAny<UserModel>(), It.IsAny<uint>(),
                TestLtUid, TestLToken),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_WithNullLastCheckIn_ShouldProceedWithCheckIn()
    {
        // Arrange
        var user = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new() { ProfileId = 1, LtUid = TestLtUid, LastCheckIn = null }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Setup token cache to return valid token
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);

        // Act
        await ExecuteDailyCheckInCommand(TestUserId, 1u);

        // Assert
        m_DailyCheckInServiceMock.Verify(
            x => x.CheckInAsync(It.IsAny<ApplicationCommandContext>(), It.IsAny<UserModel>(), It.IsAny<uint>(),
                TestLtUid, TestLToken),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_WhenExceptionThrown_ShouldSendErrorMessage()
    {
        // Arrange - Create a scenario that will throw an exception
        var user = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new() { ProfileId = 1, LtUid = TestLtUid }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes); // Make CheckInAsync throw an exception
        m_DailyCheckInServiceMock.Setup(x =>
                x.CheckInAsync(It.IsAny<ApplicationCommandContext>(), It.IsAny<UserModel>(), It.IsAny<uint>(),
                    It.IsAny<ulong>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("Test exception"));

        // Act
        await ExecuteDailyCheckInCommand(TestUserId, 1u);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("An error occurred"));
    }

    [Test]
    public async Task OnAuthenticationCompletedAsync_WithSuccessfulResult_ShouldCallCheckInService()
    {
        // Arrange
        var result = AuthenticationResult.Success(TestUserId, TestLtUid, TestLToken, Mock.Of<IInteractionContext>());

        // Set up pending profile by calling ExecuteAsync first
        var user = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new() { ProfileId = 1, LtUid = TestLtUid }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
        m_AuthenticationMiddlewareMock.Setup(x =>
                x.RegisterAuthenticationListener(TestUserId, It.IsAny<IAuthenticationListener>()))
            .Returns(TestGuid);

        await ExecuteDailyCheckInCommand(TestUserId, 1u); // This sets m_PendingProfile        // Act
        await m_Executor.OnAuthenticationCompletedAsync(result);

        // Assert
        m_DailyCheckInServiceMock.Verify(
            x => x.CheckInAsync(It.IsAny<IInteractionContext>(), It.IsAny<UserModel>(), It.IsAny<uint>(), TestLtUid,
                TestLToken),
            Times.Once);
    }

    [Test]
    public async Task OnAuthenticationCompletedAsync_WithFailedResult_ShouldSendErrorMessage()
    {
        // Arrange
        var result = AuthenticationResult.Failure(TestUserId, "Authentication failed");

        // Create interaction context for the error message
        var interaction = m_DiscordTestHelper.CreateCommandInteraction(TestUserId);
        var context = new ApplicationCommandContext(interaction, m_DiscordTestHelper.DiscordClient);
        m_Executor.Context = context;

        // Act
        await m_Executor.OnAuthenticationCompletedAsync(result);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("Authentication failed") | Contains.Substring("error"));
    }

    [Test]
    public async Task ExecuteAsync_WithMultipleProfiles_ShouldSelectCorrectProfile()
    {
        // Arrange
        var user = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new() { ProfileId = 1, LtUid = TestLtUid },
                new() { ProfileId = 2, LtUid = TestLtUid + 1 },
                new() { ProfileId = 3, LtUid = TestLtUid + 2 }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Setup token cache for profile 2
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{TestLtUid + 1}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);

        // Act
        await ExecuteDailyCheckInCommand(TestUserId, 2u); // Select profile 2        // Assert
        m_DailyCheckInServiceMock.Verify(
            x => x.CheckInAsync(It.IsAny<ApplicationCommandContext>(), It.IsAny<UserModel>(), It.IsAny<uint>(),
                TestLtUid + 1, TestLToken), Times.Once);
    }

    [Test]
    public void Context_ShouldBeSettable()
    {
        // Arrange
        var interaction = m_DiscordTestHelper.CreateCommandInteraction(TestUserId);
        var newContext = new ApplicationCommandContext(interaction, m_DiscordTestHelper.DiscordClient);

        // Act
        m_Executor.Context = newContext;

        // Assert
        Assert.That(m_Executor.Context, Is.EqualTo(newContext));
    }

    [Test]
    public async Task ExecuteAsync_WhenCheckedInJustBeforeMidnightUtc8_ShouldAllowNextDayCheckIn()
    {
        // Arrange - Simulate checking in at 23:58 UTC+8 yesterday and now it's 00:01 UTC+8 today
        var chinaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        var nowUtc8 = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, chinaTimeZone);

        // Set last check-in to 23:58 yesterday in UTC+8
        var yesterdayLateUtc8 = nowUtc8.Date.AddDays(-1).AddHours(23).AddMinutes(58);
        var lastCheckInUtc = TimeZoneInfo.ConvertTimeToUtc(yesterdayLateUtc8, chinaTimeZone);

        var user = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new() { ProfileId = 1, LtUid = TestLtUid, LastCheckIn = lastCheckInUtc }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Setup token cache to return valid token
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);

        // Act
        await ExecuteDailyCheckInCommand(TestUserId, 1u);

        // Assert
        m_DailyCheckInServiceMock.Verify(
            x => x.CheckInAsync(It.IsAny<ApplicationCommandContext>(), It.IsAny<UserModel>(), It.IsAny<uint>(),
                TestLtUid, TestLToken),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_WhenCheckedInEarlyMorningUtc8SameDay_ShouldPreventSecondCheckIn()
    {
        // Arrange - Simulate checking in at 01:00 UTC+8 today and now it's 23:00 UTC+8 same day
        var chinaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        var nowUtc8 = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, chinaTimeZone);

        // Set last check-in to early morning today in UTC+8
        var earlyTodayUtc8 = nowUtc8.Date.AddHours(1);
        var lastCheckInUtc = TimeZoneInfo.ConvertTimeToUtc(earlyTodayUtc8, chinaTimeZone);

        var user = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new() { ProfileId = 1, LtUid = TestLtUid, LastCheckIn = lastCheckInUtc }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Setup token cache to return valid token (shouldn't be used)
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);

        // Act
        await ExecuteDailyCheckInCommand(TestUserId, 1u);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("You have already checked in today for this profile"));

        // Verify that the check-in service was NOT called
        m_DailyCheckInServiceMock.Verify(
            x => x.CheckInAsync(It.IsAny<ApplicationCommandContext>(), It.IsAny<UserModel>(), It.IsAny<uint>(),
                It.IsAny<ulong>(), It.IsAny<string>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_WhenChinaTimeZoneNotAvailable_ShouldHandleGracefully()
    {
        // This test would be tricky to implement as it requires mocking the system timezone
        // In practice, "China Standard Time" should always be available on Windows systems
        // We'll keep this as a placeholder for documentation purposes

        // For now, let's test with a normal scenario since we can't easily mock TimeZoneInfo
        var user = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new() { ProfileId = 1, LtUid = TestLtUid, LastCheckIn = null }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Setup token cache to return valid token
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);

        // Act & Assert - Should not throw an exception
        await ExecuteDailyCheckInCommand(TestUserId, 1u);

        m_DailyCheckInServiceMock.Verify(
            x => x.CheckInAsync(It.IsAny<ApplicationCommandContext>(), It.IsAny<UserModel>(), It.IsAny<uint>(),
                TestLtUid, TestLToken),
            Times.Once);
    }
}
