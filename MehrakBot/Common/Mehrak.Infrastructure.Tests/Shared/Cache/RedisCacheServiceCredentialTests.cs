﻿﻿using Mehrak.Domain.Cache;
using Mehrak.Infrastructure.Shared.Cache;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mehrak.Infrastructure.Tests.Shared.Cache;

/// <summary>
/// Decrypted credential keys must never reach durable Redis persistence. They live in process memory with absolute TTL
/// and fail closed, while all other keys keep the Redis path. </summary>
[TestFixture]
[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
internal class RedisCacheServiceCredentialTests
{
    private Mock<IDistributedCache> m_MockCache = null!;
    private RedisCacheService m_Service = null!;

    [SetUp]
    public void SetUp()
    {
        m_MockCache = new Mock<IDistributedCache>();
        m_MockCache.Setup(c => c.SetAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        m_MockCache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
        m_MockCache.Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        m_Service = new RedisCacheService(m_MockCache.Object, Mock.Of<ILogger<RedisCacheService>>());
    }

    [Test]
    [TestCase("bot:ltoken:123:456")]
    [TestCase("dashboard:ltoken:123:456")]
    public async Task SetAsync_SensitiveKey_NeverWritesToDurableCache(string key)
    {
        await m_Service.SetAsync(new CacheEntryBase<string>(key, "secret-ltoken", TimeSpan.FromMinutes(10)));

        m_MockCache.Verify(c => c.SetAsync(
            It.IsAny<string>(), It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()), Times.Never);

        var roundTrip = await m_Service.GetAsync<string>(key);
        Assert.That(roundTrip, Is.EqualTo("secret-ltoken"));
    }

    [Test]
    [TestCase("bot:ltoken:123:456")]
    [TestCase("dashboard:ltoken:123:456")]
    public async Task GetAsync_SensitiveKey_NeverReadsDurableCache(string key)
    {
        var result = await m_Service.GetAsync<string>(key);

        Assert.That(result, Is.Null);
        m_MockCache.Verify(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task GetAsync_SensitiveKey_DoesNotFallBackToDurablePlaintext()
    {
        // A pre-fix durable value must remain unreachable: reads serve memory only.
        const string key = "dashboard:ltoken:1:2";
        var durableJson = System.Text.Json.JsonSerializer.Serialize("durable-plaintext");
        m_MockCache.Setup(c => c.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(System.Text.Encoding.UTF8.GetBytes(durableJson));

        var result = await m_Service.GetAsync<string>(key);

        Assert.That(result, Is.Null);
        m_MockCache.Verify(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task SetAsync_NonSensitiveKey_StillUsesDurableCache()
    {
        await m_Service.SetAsync(new CacheEntryBase<string>("dashboard:release-notes", "v", TimeSpan.FromMinutes(1)));

        m_MockCache.Verify(c => c.SetAsync(
            "dashboard:release-notes", It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task RemoveAsync_SensitiveKey_ClearsMemoryEntry()
    {
        const string key = "bot:ltoken:9:9";
        await m_Service.SetAsync(new CacheEntryBase<string>(key, "secret", TimeSpan.FromMinutes(10)));

        await m_Service.RemoveAsync(key);

        Assert.That(await m_Service.GetAsync<string>(key), Is.Null);
    }

    [Test]
    public async Task GetAsync_SensitiveKey_TypeMismatch_DropsEntryFailClosed()
    {
        const string key = "dashboard:ltoken:7:8";
        await m_Service.SetAsync(new CacheEntryBase<string>(key, "legacy-plaintext", TimeSpan.FromMinutes(5)));

        // A ticket read over a plaintext-shaped entry must not authenticate.
        var ticket = await m_Service.GetAsync<DashboardTicketShape>(key);

        Assert.That(ticket, Is.Null);
        // Dropped: a subsequent string read is also a miss.
        Assert.That(await m_Service.GetAsync<string>(key), Is.Null);
    }

    [Test]
    public async Task GetAsync_SensitiveKey_ExpiredEntry_ReturnsNull()
    {
        const string key = "bot:ltoken:5:6";
        await m_Service.SetAsync(new CacheEntryBase<string>(key, "secret", TimeSpan.FromMilliseconds(50)));

        await Task.Delay(TimeSpan.FromMilliseconds(200));

        Assert.That(await m_Service.GetAsync<string>(key), Is.Null);
    }

    [Test]
    public async Task SetAsync_SensitiveKey_PurgesUntouchedExpiredEntries()
    {
        // Expiry is otherwise lazy (checked only when the same key is read),
        // so an unrelated write must release expired entries instead of
        // retaining decrypted credentials for the process lifetime.
        const string staleKey = "bot:ltoken:1:1";
        const string liveKey = "bot:ltoken:2:2";
        await m_Service.SetAsync(new CacheEntryBase<string>(staleKey, "stale", TimeSpan.FromMilliseconds(50)));

        await Task.Delay(TimeSpan.FromMilliseconds(200));
        await m_Service.SetAsync(new CacheEntryBase<string>(liveKey, "live", TimeSpan.FromMinutes(10)));

        Assert.That(await m_Service.GetAsync<string>(liveKey), Is.EqualTo("live"));
        // The untouched stale entry was purged by the unrelated write instead
        // of lingering until its own key is read again.
        Assert.That(m_Service.SensitiveCount, Is.EqualTo(1));
    }

    private sealed record DashboardTicketShape(string CredentialHash, string SessionHash, string LToken);
}


