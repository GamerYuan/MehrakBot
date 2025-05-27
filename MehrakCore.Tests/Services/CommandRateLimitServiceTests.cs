#region

using System.Collections;
using System.Text;
using MehrakCore.Services;
using MehrakCore.Services.Common;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

#endregion

namespace MehrakCore.Tests.Services;

[Parallelizable(ParallelScope.Fixtures)]
public class CommandRateLimitServiceTests
{
    private Mock<IDistributedCache> m_MockDistributedCache;
    private Mock<ILogger<CommandRateLimitService>> m_MockLogger;
    private CommandRateLimitService m_Service;

    [SetUp]
    public void Setup()
    {
        m_MockDistributedCache = new Mock<IDistributedCache>();
        m_MockLogger = new Mock<ILogger<CommandRateLimitService>>();

        m_Service = new CommandRateLimitService(m_MockDistributedCache.Object, m_MockLogger.Object);
    }

    [Test]
    public async Task SetRateLimitAsync_ShouldAddEntryToCache()
    {
        // Arrange
        ulong userId = 123456789;
        string expectedCacheKey = $"RateLimit_{userId}";
        byte[] expectedValueBytes = "true"u8.ToArray();
        DistributedCacheEntryOptions? capturedOptions = null;

        m_MockDistributedCache
            .Setup(m => m.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>((_, _, options, _) =>
            {
                capturedOptions = options;
            })
            .Returns(Task.CompletedTask);

        // Act
        await m_Service.SetRateLimitAsync(userId);

        // Assert
        m_MockDistributedCache.Verify(
            m => m.SetAsync(
                expectedCacheKey,
                It.Is<byte[]>(b => StructuralComparisons.StructuralEqualityComparer.Equals(b, expectedValueBytes)),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        Assert.That(capturedOptions, Is.Not.Null);
        Assert.That(capturedOptions.AbsoluteExpirationRelativeToNow, Is.Not.Null);
        Assert.That(capturedOptions.AbsoluteExpirationRelativeToNow.Value.TotalSeconds, Is.EqualTo(10).Within(0.1));
    }

    [Test]
    public async Task IsRateLimitedAsync_ShouldReturnTrue_WhenUserIsRateLimited()
    {
        // Arrange
        ulong userId = 123456789;
        string expectedCacheKey = $"RateLimit_{userId}";
        byte[] valueBytes = Encoding.UTF8.GetBytes("true");

        m_MockDistributedCache
            .Setup(m => m.GetAsync(expectedCacheKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(valueBytes);

        // Act
        bool result = await m_Service.IsRateLimitedAsync(userId);

        // Assert
        Assert.That(result, Is.True);
        m_MockDistributedCache.Verify(
            m => m.GetAsync(expectedCacheKey, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task IsRateLimitedAsync_ShouldReturnFalse_WhenUserIsNotRateLimited()
    {
        // Arrange
        ulong userId = 123456789;
        string expectedCacheKey = $"RateLimit_{userId}";

        m_MockDistributedCache
            .Setup(m => m.GetAsync(expectedCacheKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        // Act
        bool result = await m_Service.IsRateLimitedAsync(userId);

        // Assert
        Assert.That(result, Is.False);
        m_MockDistributedCache.Verify(
            m => m.GetAsync(expectedCacheKey, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    [Category("LongRunning")]
    public async Task RateLimit_ShouldExpireAfterDefaultTime()
    {
        // Arrange
        ulong userId = 1111111111;
        var options = new MemoryDistributedCacheOptions();
        var memoryDistributedCache = new MemoryDistributedCache(Options.Create(options));
        var logger = m_MockLogger.Object;
        var service = new CommandRateLimitService(memoryDistributedCache, logger);

        // Act
        await service.SetRateLimitAsync(userId);
        bool limitedBefore = await service.IsRateLimitedAsync(userId);

        // Wait for expiration (slightly longer than the 10 seconds default)
        await Task.Delay(TimeSpan.FromSeconds(11));

        bool limitedAfter = await service.IsRateLimitedAsync(userId);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(limitedBefore, Is.True);
            Assert.That(limitedAfter, Is.False);
        });
    }
}
