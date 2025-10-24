#region

using Mehrak.Infrastructure.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Text;

#endregion

namespace MehrakCore.Tests.Services.Common;

[Parallelizable(ParallelScope.Fixtures)]
public class TokenCacheServiceTests
{
    private IDistributedCache m_Cache;
    private Mock<ILogger<RedisCacheService>> m_MockLogger;
    private RedisCacheService m_Service;
    private readonly TimeSpan m_DefaultServiceExpiration = TimeSpan.FromMinutes(5); // Mirroring service's default

    [SetUp]
    public void Setup()
    {
        // Create an in-memory distributed cache for testing
        m_Cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        m_MockLogger = new Mock<ILogger<RedisCacheService>>();

        m_Service = new RedisCacheService(m_Cache, m_MockLogger.Object);
    }

    [Test]
    public async Task AddCacheEntryAsync_ShouldAddToCache()
    {
        // Arrange
        const ulong ltuid = 123456;
        const string ltoken = "test-token";
        const ulong userId = 100;
        string cacheKey = $"TokenCache_{userId}_{ltuid}";

        // Act
        await m_Service.AddCacheEntryAsync(ltuid, ltoken, 100);

        // Assert
        byte[]? cachedBytes = await m_Cache.GetAsync(cacheKey);
        Assert.That(cachedBytes, Is.Not.Null);
        string cachedToken = Encoding.UTF8.GetString(cachedBytes!);
        Assert.That(cachedToken, Is.EqualTo(ltoken));
    }

    [Test]
    public async Task GetCacheEntry_WhenValueExists_ReturnsToken()
    {
        // Arrange
        const ulong userId = 100;
        const ulong ltuid = 123456;
        const string ltoken = "test-token";
        string cacheKey = $"TokenCache_{userId}_{ltuid}";

        // Add the entry directly to the cache
        byte[] tokenBytes = Encoding.UTF8.GetBytes(ltoken);
        await m_Cache.SetAsync(cacheKey, tokenBytes, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = m_DefaultServiceExpiration
        });

        // Act
        string? retrievedToken = await m_Service.GetCacheEntry(userId, ltuid);

        // Assert
        Assert.That(retrievedToken, Is.Not.Null);
        Assert.That(retrievedToken, Is.EqualTo(ltoken));

        m_MockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Found")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()!),
            Times.Once);
    }

    [Test]
    public async Task GetCacheEntry_WhenValueDoesNotExist_ReturnsNull()
    {
        // Arrange
        const ulong userId = 100;
        const ulong ltuid = 789012; // Different ltuid, not in cache

        // Act
        string? retrievedToken = await m_Service.GetCacheEntry(userId, ltuid);

        // Assert
        Assert.That(retrievedToken, Is.Null);

        m_MockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Not Found")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()!),
            Times.Once);
    }

    [Test]
    public async Task AddCacheEntryAsync_SetsEntryWhichCanBeRetrieved()
    {
        // Arrange
        const ulong ltuid = 123456;
        const string ltoken = "test-token";
        const ulong userId = 100; // Any user ID for testing

        MemoryDistributedCache testCache = new(Options.Create(new MemoryDistributedCacheOptions()));
        Mock<ILogger<RedisCacheService>> logger = new();
        RedisCacheService service = new(testCache, logger.Object);

        // Act
        await service.AddCacheEntryAsync(ltuid, ltoken, userId);

        // Assert - Entry exists initially
        string? token = await service.GetCacheEntry(userId, ltuid);
        Assert.That(token, Is.Not.Null);
        Assert.That(token, Is.EqualTo(ltoken));
    }
}
