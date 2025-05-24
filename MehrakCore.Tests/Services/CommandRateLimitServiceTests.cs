#region

using MehrakCore.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;

#endregion

namespace MehrakCore.Tests.Services;

[Parallelizable(ParallelScope.Fixtures)]
public class CommandRateLimitServiceTests
{
    private Mock<IMemoryCache> m_MockCache;
    private Mock<ILogger<CommandRateLimitService>> m_MockLogger;
    private CommandRateLimitService m_Service;
    private Mock<ICacheEntry> m_MockCacheEntry;

    [SetUp]
    public void Setup()
    {
        m_MockCache = new Mock<IMemoryCache>();
        m_MockLogger = new Mock<ILogger<CommandRateLimitService>>();
        m_MockCacheEntry = new Mock<ICacheEntry>();

        // Setup CreateEntry instead of Set
        m_MockCache
            .Setup(m => m.CreateEntry(It.IsAny<object>()))
            .Returns(m_MockCacheEntry.Object);

        m_Service = new CommandRateLimitService(m_MockCache.Object, m_MockLogger.Object);
    }

    [Test]
    public void SetRateLimit_ShouldAddEntryToCache()
    {
        // Arrange
        ulong userId = 123456789;
        object capturedValue = null;

        // Setup to capture the value
        m_MockCacheEntry
            .SetupSet(e => e.Value = It.IsAny<object>())
            .Callback<object>(v => capturedValue = v);

        // Act
        m_Service.SetRateLimit(userId);

        // Assert
        m_MockCache.Verify(m => m.CreateEntry(userId.ToString()), Times.Once);
        Assert.That(capturedValue, Is.Not.Null);
        Assert.That(capturedValue, Is.EqualTo(true));
        m_MockCacheEntry.Verify(e => e.Dispose(), Times.Once);
    }

    [Test]
    public void SetRateLimit_ShouldSetAbsoluteExpiration()
    {
        // Arrange
        ulong userId = 123456789;

        // Act
        m_Service.SetRateLimit(userId);

        // Assert
        m_MockCacheEntry.VerifySet(e =>
            e.AbsoluteExpirationRelativeToNow =
                It.Is<TimeSpan?>(ts => ts.HasValue && Math.Abs(ts.Value.TotalSeconds - 10) < 0.1));
    }

    [Test]
    public void IsRateLimited_ShouldReturnTrue_WhenUserIsRateLimited()
    {
        // Arrange
        ulong userId = 123456789;
        object? outValue = true;

        m_MockCache
            .Setup(m => m.TryGetValue(userId.ToString(), out outValue))
            .Returns(true);

        // Act
        bool result = m_Service.IsRateLimited(userId);

        // Assert
        Assert.That(result, Is.True);
        m_MockCache.Verify(m => m.TryGetValue(userId.ToString(), out outValue), Times.Once);
    }

    [Test]
    public void IsRateLimited_ShouldReturnFalse_WhenUserIsNotRateLimited()
    {
        // Arrange
        ulong userId = 123456789;
        object? outValue = null;

        m_MockCache
            .Setup(m => m.TryGetValue(userId.ToString(), out outValue))
            .Returns(false);

        // Act
        bool result = m_Service.IsRateLimited(userId);

        // Assert
        Assert.That(result, Is.False);
        m_MockCache.Verify(m => m.TryGetValue(userId.ToString(), out outValue), Times.Once);
    }

    [Test]
    [Category("LongRunning")]
    [Parallelizable(ParallelScope.Self)]
    public void RateLimit_ShouldExpireAfterDefaultTime()
    {
        // Arrange
        ulong userId = 1111111111;
        var actualMemoryCache = new MemoryCache(new MemoryCacheOptions());
        var logger = m_MockLogger.Object;
        var service = new CommandRateLimitService(actualMemoryCache, logger);

        // Act
        service.SetRateLimit(userId);
        bool limitedBefore = service.IsRateLimited(userId);

        // Wait for expiration (slightly longer than the 10 seconds default)
        Thread.Sleep(TimeSpan.FromSeconds(11));

        bool limitedAfter = service.IsRateLimited(userId);

        // Assert
        Assert.That(limitedBefore, Is.True);
        Assert.That(limitedAfter, Is.False);

        // Clean up
        actualMemoryCache.Dispose();
    }
}
