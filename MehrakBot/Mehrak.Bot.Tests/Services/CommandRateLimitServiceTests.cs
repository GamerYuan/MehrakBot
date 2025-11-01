#region

using Mehrak.Bot.Services;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.Domain.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

#endregion

namespace Mehrak.Bot.Tests.Services;

/// <summary>
/// Unit tests for CommandRateLimitService validating rate limiting logic,
/// cache interactions, expiration handling, and concurrent user scenarios.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class CommandRateLimitServiceTests
{
    private Mock<ICacheService> m_MockCacheService = null!;
    private CommandRateLimitService m_Service = null!;

    private const ulong TestUserId = 123456789UL;
    private const ulong TestUserId2 = 987654321UL;
    private static readonly TimeSpan ExpectedExpirationTime = TimeSpan.FromSeconds(10);

    [SetUp]
    public void Setup()
    {
        m_MockCacheService = new Mock<ICacheService>();
        m_Service = new CommandRateLimitService(
            m_MockCacheService.Object,
            NullLogger<CommandRateLimitService>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        m_MockCacheService.Reset();
    }

    #region IsRateLimitedAsync Tests - Basic Functionality

    [Test]
    public async Task IsRateLimitedAsync_WithNoRateLimit_ReturnsFalse()
    {
        // Arrange
        m_MockCacheService
            .Setup(x => x.GetAsync<string>(It.IsAny<string>()))
            .ReturnsAsync((string?)null);

        // Act
        var result = await m_Service.IsRateLimitedAsync(TestUserId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task IsRateLimitedAsync_WithActiveRateLimit_ReturnsTrue()
    {
        // Arrange
        m_MockCacheService
            .Setup(x => x.GetAsync<string>(It.IsAny<string>()))
            .ReturnsAsync("1");

        // Act
        var result = await m_Service.IsRateLimitedAsync(TestUserId);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task IsRateLimitedAsync_CallsCacheWithCorrectKey()
    {
        // Arrange
        var expectedKey = $"cmd_rate_limit:{TestUserId}";
        m_MockCacheService
            .Setup(x => x.GetAsync<string>(expectedKey))
            .ReturnsAsync((string?)null);

        // Act
        await m_Service.IsRateLimitedAsync(TestUserId);

        // Assert
        m_MockCacheService.Verify(x => x.GetAsync<string>(expectedKey), Times.Once);
    }

    [Test]
    public async Task IsRateLimitedAsync_WithDifferentUserIds_UsesCorrectKeys()
    {
        // Arrange
        var key1 = $"cmd_rate_limit:{TestUserId}";
        var key2 = $"cmd_rate_limit:{TestUserId2}";

        m_MockCacheService
            .Setup(x => x.GetAsync<string>(It.IsAny<string>()))
            .ReturnsAsync((string?)null);

        // Act
        await m_Service.IsRateLimitedAsync(TestUserId);
        await m_Service.IsRateLimitedAsync(TestUserId2);

        // Assert
        m_MockCacheService.Verify(x => x.GetAsync<string>(key1), Times.Once);
        m_MockCacheService.Verify(x => x.GetAsync<string>(key2), Times.Once);
    }

    #endregion

    #region IsRateLimitedAsync Tests - Cache Value Variations

    [Test]
    public async Task IsRateLimitedAsync_WithAnyNonNullValue_ReturnsTrue()
    {
        // Arrange
        m_MockCacheService
            .Setup(x => x.GetAsync<string>(It.IsAny<string>()))
            .ReturnsAsync("any_value");

        // Act
        var result = await m_Service.IsRateLimitedAsync(TestUserId);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task IsRateLimitedAsync_WithEmptyString_ReturnsTrue()
    {
        // Arrange
        m_MockCacheService
            .Setup(x => x.GetAsync<string>(It.IsAny<string>()))
            .ReturnsAsync("");

        // Act
        var result = await m_Service.IsRateLimitedAsync(TestUserId);

        // Assert
        Assert.That(result, Is.True);
    }

    #endregion

    #region SetRateLimitAsync Tests - Basic Functionality

    [Test]
    public async Task SetRateLimitAsync_CallsCacheSetWithCorrectKey()
    {
        // Arrange
        var expectedKey = $"cmd_rate_limit:{TestUserId}";

        // Act
        await m_Service.SetRateLimitAsync(TestUserId);

        // Assert
        m_MockCacheService.Verify(
            x => x.SetAsync(It.Is<ICacheEntry<string>>(e => e.Key == expectedKey)),
            Times.Once);
    }

    [Test]
    public async Task SetRateLimitAsync_SetsValueTo1()
    {
        // Arrange & Act
        await m_Service.SetRateLimitAsync(TestUserId);

        // Assert
        m_MockCacheService.Verify(
            x => x.SetAsync(It.Is<ICacheEntry<string>>(e => e.Value == "1")),
            Times.Once);
    }

    [Test]
    public async Task SetRateLimitAsync_SetsExpiration10Seconds()
    {
        // Arrange & Act
        await m_Service.SetRateLimitAsync(TestUserId);

        // Assert
        m_MockCacheService.Verify(
            x => x.SetAsync(It.Is<ICacheEntry<string>>(e => e.ExpirationTime == ExpectedExpirationTime)),
            Times.Once);
    }

    [Test]
    public async Task SetRateLimitAsync_CreatesCacheEntryWithAllProperties()
    {
        // Arrange
        var expectedKey = $"cmd_rate_limit:{TestUserId}";

        // Act
        await m_Service.SetRateLimitAsync(TestUserId);

        // Assert
        m_MockCacheService.Verify(
            x => x.SetAsync(It.Is<ICacheEntry<string>>(e =>
                e.Key == expectedKey &&
                e.Value == "1" &&
                e.ExpirationTime == ExpectedExpirationTime)),
            Times.Once);
    }

    [Test]
    public async Task SetRateLimitAsync_ForDifferentUsers_CreatesDifferentKeys()
    {
        // Arrange
        var key1 = $"cmd_rate_limit:{TestUserId}";
        var key2 = $"cmd_rate_limit:{TestUserId2}";

        // Act
        await m_Service.SetRateLimitAsync(TestUserId);
        await m_Service.SetRateLimitAsync(TestUserId2);

        // Assert
        m_MockCacheService.Verify(x => x.SetAsync(It.Is<ICacheEntry<string>>(e => e.Key == key1)), Times.Once);
        m_MockCacheService.Verify(x => x.SetAsync(It.Is<ICacheEntry<string>>(e => e.Key == key2)), Times.Once);
    }

    #endregion

    #region Integration Tests - Set and Check Flow

    [Test]
    public async Task SetRateLimit_ThenCheck_ReturnsTrue()
    {
        // Arrange
        string? cachedValue = null;
        m_MockCacheService
            .Setup(x => x.SetAsync(It.IsAny<ICacheEntry<string>>()))
            .Callback<ICacheEntry<string>>(entry => cachedValue = entry.Value)
            .Returns(Task.CompletedTask);

        m_MockCacheService
            .Setup(x => x.GetAsync<string>(It.IsAny<string>()))
            .ReturnsAsync(() => cachedValue);

        // Act
        await m_Service.SetRateLimitAsync(TestUserId);
        var result = await m_Service.IsRateLimitedAsync(TestUserId);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task BeforeSetRateLimit_CheckReturns_False()
    {
        // Arrange
        m_MockCacheService
            .Setup(x => x.GetAsync<string>(It.IsAny<string>()))
            .ReturnsAsync((string?)null);

        // Act
        var resultBefore = await m_Service.IsRateLimitedAsync(TestUserId);

        // Assert
        Assert.That(resultBefore, Is.False);
    }

    [Test]
    public async Task AfterSetRateLimit_CheckReturns_True()
    {
        // Arrange
        m_MockCacheService
            .Setup(x => x.GetAsync<string>(It.IsAny<string>()))
            .ReturnsAsync("1");

        m_MockCacheService
            .Setup(x => x.SetAsync(It.IsAny<ICacheEntry<string>>()))
            .Returns(Task.CompletedTask);

        // Act
        await m_Service.SetRateLimitAsync(TestUserId);
        var resultAfter = await m_Service.IsRateLimitedAsync(TestUserId);

        // Assert
        Assert.That(resultAfter, Is.True);
    }

    #endregion

    #region Concurrent User Tests

    [Test]
    public async Task MultipleUsers_CanBeRateLimitedIndependently()
    {
        // Arrange
        var user1Key = $"cmd_rate_limit:{TestUserId}";
        var user2Key = $"cmd_rate_limit:{TestUserId2}";

        m_MockCacheService
            .Setup(x => x.GetAsync<string>(user1Key))
            .ReturnsAsync("1");

        m_MockCacheService
            .Setup(x => x.GetAsync<string>(user2Key))
            .ReturnsAsync((string?)null);

        // Act
        var user1Limited = await m_Service.IsRateLimitedAsync(TestUserId);
        var user2Limited = await m_Service.IsRateLimitedAsync(TestUserId2);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(user1Limited, Is.True, "User 1 should be rate limited");
            Assert.That(user2Limited, Is.False, "User 2 should not be rate limited");
        });
    }

    [Test]
    public async Task SetRateLimitForOneUser_DoesNotAffectOtherUsers()
    {
        // Arrange
        var user1Key = $"cmd_rate_limit:{TestUserId}";
        var user2Key = $"cmd_rate_limit:{TestUserId2}";

        string? user1Cache = null;
        string? user2Cache = null;

        m_MockCacheService
            .Setup(x => x.SetAsync(It.Is<ICacheEntry<string>>(e => e.Key == user1Key)))
            .Callback<ICacheEntry<string>>(entry => user1Cache = entry.Value)
            .Returns(Task.CompletedTask);

        m_MockCacheService
            .Setup(x => x.GetAsync<string>(user1Key))
            .ReturnsAsync(() => user1Cache);

        m_MockCacheService
            .Setup(x => x.GetAsync<string>(user2Key))
            .ReturnsAsync(() => user2Cache);

        // Act
        await m_Service.SetRateLimitAsync(TestUserId);
        var user1Limited = await m_Service.IsRateLimitedAsync(TestUserId);
        var user2Limited = await m_Service.IsRateLimitedAsync(TestUserId2);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(user1Limited, Is.True);
            Assert.That(user2Limited, Is.False);
        });
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task IsRateLimitedAsync_CalledMultipleTimes_ReturnsSameResult()
    {
        // Arrange
        m_MockCacheService
            .Setup(x => x.GetAsync<string>(It.IsAny<string>()))
            .ReturnsAsync("1");

        // Act
        var result1 = await m_Service.IsRateLimitedAsync(TestUserId);
        var result2 = await m_Service.IsRateLimitedAsync(TestUserId);
        var result3 = await m_Service.IsRateLimitedAsync(TestUserId);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result1, Is.True);
            Assert.That(result2, Is.True);
            Assert.That(result3, Is.True);
        });

        m_MockCacheService.Verify(x => x.GetAsync<string>(It.IsAny<string>()), Times.Exactly(3));
    }

    [Test]
    public async Task SetRateLimitAsync_CalledMultipleTimes_CallsCacheEachTime()
    {
        // Arrange & Act
        await m_Service.SetRateLimitAsync(TestUserId);
        await m_Service.SetRateLimitAsync(TestUserId);
        await m_Service.SetRateLimitAsync(TestUserId);

        // Assert
        m_MockCacheService.Verify(x => x.SetAsync(It.IsAny<ICacheEntry<string>>()), Times.Exactly(3));
    }

    [Test]
    public async Task IsRateLimitedAsync_WithZeroUserId_HandlesCorrectly()
    {
        // Arrange
        const ulong zeroUserId = 0UL;
        var expectedKey = $"cmd_rate_limit:{zeroUserId}";

        m_MockCacheService
            .Setup(x => x.GetAsync<string>(expectedKey))
            .ReturnsAsync((string?)null);

        // Act
        var result = await m_Service.IsRateLimitedAsync(zeroUserId);

        // Assert
        Assert.That(result, Is.False);
        m_MockCacheService.Verify(x => x.GetAsync<string>(expectedKey), Times.Once);
    }

    [Test]
    public async Task SetRateLimitAsync_WithZeroUserId_HandlesCorrectly()
    {
        // Arrange
        const ulong zeroUserId = 0UL;
        var expectedKey = $"cmd_rate_limit:{zeroUserId}";

        // Act
        await m_Service.SetRateLimitAsync(zeroUserId);

        // Assert
        m_MockCacheService.Verify(
            x => x.SetAsync(It.Is<ICacheEntry<string>>(e => e.Key == expectedKey)),
            Times.Once);
    }

    [Test]
    public async Task IsRateLimitedAsync_WithMaxUserId_HandlesCorrectly()
    {
        // Arrange
        const ulong maxUserId = ulong.MaxValue;
        var expectedKey = $"cmd_rate_limit:{maxUserId}";

        m_MockCacheService
            .Setup(x => x.GetAsync<string>(expectedKey))
            .ReturnsAsync("1");

        // Act
        var result = await m_Service.IsRateLimitedAsync(maxUserId);

        // Assert
        Assert.That(result, Is.True);
        m_MockCacheService.Verify(x => x.GetAsync<string>(expectedKey), Times.Once);
    }

    #endregion

    #region Cache Service Interaction Tests

    [Test]
    public async Task IsRateLimitedAsync_CallsCacheGetOnce()
    {
        // Arrange
        m_MockCacheService
            .Setup(x => x.GetAsync<string>(It.IsAny<string>()))
            .ReturnsAsync((string?)null);

        // Act
        await m_Service.IsRateLimitedAsync(TestUserId);

        // Assert
        m_MockCacheService.Verify(x => x.GetAsync<string>(It.IsAny<string>()), Times.Once);
    }

    [Test]
    public async Task SetRateLimitAsync_CallsCacheSetOnce()
    {
        // Arrange & Act
        await m_Service.SetRateLimitAsync(TestUserId);

        // Assert
        m_MockCacheService.Verify(x => x.SetAsync(It.IsAny<ICacheEntry<string>>()), Times.Once);
    }

    [Test]
    public async Task IsRateLimitedAsync_DoesNotCallCacheSet()
    {
        // Arrange
        m_MockCacheService
            .Setup(x => x.GetAsync<string>(It.IsAny<string>()))
            .ReturnsAsync((string?)null);

        // Act
        await m_Service.IsRateLimitedAsync(TestUserId);

        // Assert
        m_MockCacheService.Verify(x => x.SetAsync(It.IsAny<ICacheEntry<string>>()), Times.Never);
    }

    [Test]
    public async Task SetRateLimitAsync_DoesNotCallCacheGet()
    {
        // Arrange & Act
        await m_Service.SetRateLimitAsync(TestUserId);

        // Assert
        m_MockCacheService.Verify(x => x.GetAsync<string>(It.IsAny<string>()), Times.Never);
    }

    #endregion

    #region Key Format Tests

    [Test]
    public async Task CacheKey_HasCorrectPrefix()
    {
        // Arrange
        m_MockCacheService
            .Setup(x => x.GetAsync<string>(It.IsAny<string>()))
            .ReturnsAsync((string?)null);

        // Act
        await m_Service.IsRateLimitedAsync(TestUserId);

        // Assert
        m_MockCacheService.Verify(
            x => x.GetAsync<string>(It.Is<string>(key => key.StartsWith("cmd_rate_limit:"))),
            Times.Once);
    }

    [Test]
    public async Task CacheKey_IncludesUserId()
    {
        // Arrange
        m_MockCacheService
            .Setup(x => x.GetAsync<string>(It.IsAny<string>()))
            .ReturnsAsync((string?)null);

        // Act
        await m_Service.IsRateLimitedAsync(TestUserId);

        // Assert
        m_MockCacheService.Verify(
            x => x.GetAsync<string>(It.Is<string>(key => key.Contains(TestUserId.ToString()))),
            Times.Once);
    }

    [Test]
    public async Task CacheKey_Format_IsConsistentBetweenSetAndGet()
    {
        // Arrange
        string? setKey = null;
        string? getKey = null;

        m_MockCacheService
            .Setup(x => x.SetAsync(It.IsAny<ICacheEntry<string>>()))
            .Callback<ICacheEntry<string>>(entry => setKey = entry.Key)
            .Returns(Task.CompletedTask);

        m_MockCacheService
            .Setup(x => x.GetAsync<string>(It.IsAny<string>()))
            .Callback<string>(key => getKey = key)
            .ReturnsAsync((string?)null);

        // Act
        await m_Service.SetRateLimitAsync(TestUserId);
        await m_Service.IsRateLimitedAsync(TestUserId);

        // Assert
        Assert.That(setKey, Is.EqualTo(getKey));
    }

    #endregion

    #region Logging Tests

    [Test]
    public async Task IsRateLimitedAsync_WhenRateLimited_LogsDebugMessage()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<CommandRateLimitService>>();
        var serviceWithLogger = new CommandRateLimitService(m_MockCacheService.Object, mockLogger.Object);

        m_MockCacheService
            .Setup(x => x.GetAsync<string>(It.IsAny<string>()))
            .ReturnsAsync("1");

        // Act
        await serviceWithLogger.IsRateLimitedAsync(TestUserId);

        // Assert
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(TestUserId.ToString())),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public async Task IsRateLimitedAsync_WhenNotRateLimited_DoesNotLog()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<CommandRateLimitService>>();
        var serviceWithLogger = new CommandRateLimitService(m_MockCacheService.Object, mockLogger.Object);

        m_MockCacheService
            .Setup(x => x.GetAsync<string>(It.IsAny<string>()))
            .ReturnsAsync((string?)null);

        // Act
        await serviceWithLogger.IsRateLimitedAsync(TestUserId);

        // Assert
        mockLogger.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Test]
    public async Task SetRateLimitAsync_LogsDebugMessage()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<CommandRateLimitService>>();
        var serviceWithLogger = new CommandRateLimitService(m_MockCacheService.Object, mockLogger.Object);

        // Act
        await serviceWithLogger.SetRateLimitAsync(TestUserId);

        // Assert
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(TestUserId.ToString())),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region Real-World Scenarios

    [Test]
    public async Task RateLimitScenario_UserNotLimited_ThenSetLimit_ThenCheckAgain()
    {
        // Arrange
        string? cachedValue = null;

        m_MockCacheService
            .Setup(x => x.SetAsync(It.IsAny<ICacheEntry<string>>()))
            .Callback<ICacheEntry<string>>(entry => cachedValue = entry.Value)
            .Returns(Task.CompletedTask);

        m_MockCacheService
            .Setup(x => x.GetAsync<string>(It.IsAny<string>()))
            .ReturnsAsync(() => cachedValue);

        // Act & Assert
        var beforeLimit = await m_Service.IsRateLimitedAsync(TestUserId);
        Assert.That(beforeLimit, Is.False, "User should not be limited initially");

        await m_Service.SetRateLimitAsync(TestUserId);

        var afterLimit = await m_Service.IsRateLimitedAsync(TestUserId);
        Assert.That(afterLimit, Is.True, "User should be limited after setting");
    }

    [Test]
    public async Task MultipleUserScenario_DifferentRateLimitStates()
    {
        // Arrange
        var key1 = $"cmd_rate_limit:{TestUserId}";
        var key2 = $"cmd_rate_limit:{TestUserId2}";
        const ulong userId3 = 111222333UL;
        var key3 = $"cmd_rate_limit:{userId3}";

        m_MockCacheService.Setup(x => x.GetAsync<string>(key1)).ReturnsAsync("1");
        m_MockCacheService.Setup(x => x.GetAsync<string>(key2)).ReturnsAsync((string?)null);
        m_MockCacheService.Setup(x => x.GetAsync<string>(key3)).ReturnsAsync("1");

        // Act
        var user1Limited = await m_Service.IsRateLimitedAsync(TestUserId);
        var user2Limited = await m_Service.IsRateLimitedAsync(TestUserId2);
        var user3Limited = await m_Service.IsRateLimitedAsync(userId3);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(user1Limited, Is.True, "User 1 should be limited");
            Assert.That(user2Limited, Is.False, "User 2 should not be limited");
            Assert.That(user3Limited, Is.True, "User 3 should be limited");
        });
    }

    #endregion
}
