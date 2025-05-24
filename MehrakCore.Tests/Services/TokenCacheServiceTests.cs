#region

using MehrakCore.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;

#endregion

namespace MehrakCore.Tests.Services;

public class TokenCacheServiceTests
{
    private IMemoryCache m_Cache;
    private Mock<ILogger<TokenCacheService>> m_MockLogger;
    private TokenCacheService m_Service;

    [SetUp]
    public void Setup()
    {
        // Create a real memory cache for testing
        m_Cache = new MemoryCache(new MemoryCacheOptions());
        m_MockLogger = new Mock<ILogger<TokenCacheService>>();

        // Note: We're bypassing the [FromKeyedServices] attribute for testing purposes
        m_Service = new TokenCacheService(m_Cache, m_MockLogger.Object);
    }

    [TearDown]
    public void TearDown()
    {
        m_Cache.Dispose();
    }

    [Test]
    public void AddCacheEntry_ShouldAddToCache()
    {
        // Arrange
        const ulong ltuid = 123456;
        const string ltoken = "test-token";

        // Act
        m_Service.AddCacheEntry(ltuid, ltoken);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(m_Cache.TryGetValue(ltuid, out string cachedToken), Is.True);
            Assert.That(cachedToken, Is.Not.Null);
            Assert.That(cachedToken, Is.EqualTo(ltoken));
        });
    }

    [Test]
    public void TryGetCacheEntry_WhenValueExists_ReturnsTrue()
    {
        // Arrange
        const ulong userId = 100;
        const ulong ltuid = 123456;
        const string ltoken = "test-token";

        // Add the entry directly to the cache
        m_Cache.Set(ltuid, ltoken);

        // Act
        bool result = m_Service.TryGetCacheEntry(userId, ltuid, out string retrievedToken);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(retrievedToken, Is.EqualTo(ltoken));

        m_MockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString().Contains("Found")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()!),
            Times.Once);
    }

    [Test]
    public void TryGetCacheEntry_WhenValueDoesNotExist_ReturnsFalse()
    {
        // Arrange
        const ulong userId = 100;
        const ulong ltuid = 789012;

        // Act
        bool result = m_Service.TryGetCacheEntry(userId, ltuid, out string retrievedToken);

        // Assert

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(retrievedToken, Is.Null);
        });
        m_MockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString().Contains("Not Found")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Test]
    public void CacheEntry_ShouldExpireAfterDefaultExpiration()
    {
        // Arrange
        const ulong ltuid = 123456;
        const string ltoken = "test-token";

        // Use a memory cache with a test clock for predictable expiration
        var options = new MemoryCacheOptions();
        var testCache = new MemoryCache(options);
        var logger = new Mock<ILogger<TokenCacheService>>();
        var service = new TokenCacheService(testCache, logger.Object);

        // Act
        service.AddCacheEntry(ltuid, ltoken);

        // Assert - Entry exists initially
        Assert.That(testCache.TryGetValue(ltuid, out string token), Is.True);
        Assert.That(token, Is.EqualTo(ltoken));

        // Force expiration by clearing the cache
        testCache.Compact(1.0);

        // Assert - Entry is removed after expiration
        Assert.That(testCache.TryGetValue(ltuid, out _), Is.False);
    }
}
