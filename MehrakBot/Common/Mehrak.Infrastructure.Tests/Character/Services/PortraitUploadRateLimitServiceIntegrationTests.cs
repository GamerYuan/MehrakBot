using Mehrak.Infrastructure.Character.Services;
using Mehrak.Infrastructure.Tests.TestUtils;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;

namespace Mehrak.Infrastructure.Tests.Character.Services;

/// <summary>
/// Finding 12: against real disposable Redis with a fixed timestamp,
/// simultaneous portrait uploads are counted individually and allowed work
/// never exceeds the five-upload quota. Requires Docker; runs in CI.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
internal class PortraitUploadRateLimitServiceIntegrationTests
{
    private static readonly DateTimeOffset FixedTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private IConnectionMultiplexer m_Multiplexer = null!;
    private PortraitUploadRateLimitService m_Limiter = null!;
    private long m_UserId;

    [SetUp]
    public void SetUp()
    {
        m_Multiplexer = RedisTestHelper.Instance.CreateMultiplexer();
        m_Limiter = new PortraitUploadRateLimitService(
            m_Multiplexer, Mock.Of<ILogger<PortraitUploadRateLimitService>>())
        {
            UtcNowProvider = () => FixedTime
        };
        m_UserId = Random.Shared.NextInt64(1_000_000_000, long.MaxValue);
    }

    [TearDown]
    public void TearDown() => m_Multiplexer.Dispose();

    [Test]
    public async Task SimultaneousUploadsAtSameTimestamp_CountIndividually()
    {
        for (var i = 0; i < 5; i++)
            Assert.That(await m_Limiter.IsAllowedAsync(m_UserId), Is.True);

        Assert.Multiple(async () =>
        {
            Assert.That(await m_Limiter.IsAllowedAsync(m_UserId), Is.False);
            Assert.That(await m_Limiter.GetRemainingAsync(m_UserId), Is.EqualTo(0));
        });
    }
}
