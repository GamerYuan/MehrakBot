using Mehrak.Infrastructure.Models;
using Mehrak.Infrastructure.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mehrak.Infrastructure.Tests.IntegrationTests.Services;

[TestFixture]
internal class RedisCacheServiceIntegrationTests
{
    private static CacheEntryBase<T> CreateEntry<T>(T value, TimeSpan? expiration = null)
    {
        return new CacheEntryBase<T>($"redis:integration:{Guid.NewGuid():N}", value, expiration ?? TimeSpan.FromMinutes(1));
    }

    [Test]
    public async Task SetAsync_ThenGetAsync_RoundTripsValue()
    {
        using var cache = RedisTestHelper.Instance.CreateCache();
        var service = new RedisCacheService(cache, NullLogger<RedisCacheService>.Instance);
        var entry = CreateEntry("roundtrip-value");

        await service.SetAsync(entry);
        var result = await service.GetAsync<string>(entry.Key);

        Assert.That(result, Is.EqualTo(entry.Value));
    }

    [Test]
    public async Task RemoveAsync_RemovesStoredValue()
    {
        using var cache = RedisTestHelper.Instance.CreateCache();
        var service = new RedisCacheService(cache, NullLogger<RedisCacheService>.Instance);
        var entry = CreateEntry("to-remove");

        await service.SetAsync(entry);
        Assert.That(await cache.GetStringAsync(entry.Key), Is.Not.Null);

        await service.RemoveAsync(entry.Key);
        var remaining = await cache.GetStringAsync(entry.Key);

        Assert.That(remaining, Is.Null);
    }

    [Test]
    public async Task SetAsync_RespectsExpiration()
    {
        using var cache = RedisTestHelper.Instance.CreateCache();
        var service = new RedisCacheService(cache, NullLogger<RedisCacheService>.Instance);
        var entry = CreateEntry("expiring", TimeSpan.FromSeconds(1));

        await service.SetAsync(entry);
        await Task.Delay(TimeSpan.FromSeconds(2));

        var result = await service.GetAsync<string>(entry.Key);

        Assert.That(result, Is.Null);
    }
}
