using Mehrak.Infrastructure.Shared;
using Mehrak.Infrastructure.Tests.TestUtils;
using StackExchange.Redis;

namespace Mehrak.Infrastructure.Tests.Shared;

/// <summary>
/// Finding 12: against real disposable Redis with a fixed timestamp,
/// simultaneous passphrase attempts are counted individually and allowed
/// work never exceeds the five-attempt quota. Requires Docker; runs in CI.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
internal class PassphraseAttemptRateLimiterIntegrationTests
{
    private static readonly DateTimeOffset FixedTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private IConnectionMultiplexer m_Multiplexer = null!;
    private PassphraseAttemptRateLimiter m_Limiter = null!;
    private ulong m_UserId;

    [SetUp]
    public void SetUp()
    {
        m_Multiplexer = RedisTestHelper.Instance.CreateMultiplexer();
        m_Limiter = new PassphraseAttemptRateLimiter(m_Multiplexer)
        {
            UtcNowProvider = () => FixedTime
        };
        m_UserId = (ulong)Random.Shared.NextInt64(1_000_000_000, long.MaxValue);
    }

    [TearDown]
    public void TearDown() => m_Multiplexer.Dispose();

    [Test]
    public async Task SimultaneousFailuresAtSameTimestamp_CountIndividually()
    {
        for (var i = 0; i < 5; i++)
            await m_Limiter.RecordFailureAsync(m_UserId);

        Assert.Multiple(async () =>
        {
            Assert.That(await m_Limiter.GetRemainingAttemptsAsync(m_UserId), Is.EqualTo(0));
            Assert.That(await m_Limiter.IsBlockedAsync(m_UserId), Is.True);
        });
    }

    [Test]
    public async Task SixthReservation_Blocked_QuotaNeverExceeded()
    {
        for (var i = 0; i < 5; i++)
            Assert.That(await m_Limiter.TryReserveAttemptAsync(m_UserId), Is.Not.Null);

        Assert.Multiple(async () =>
        {
            Assert.That(await m_Limiter.TryReserveAttemptAsync(m_UserId), Is.Null);
            Assert.That(await m_Limiter.GetRemainingAttemptsAsync(m_UserId), Is.EqualTo(0));
        });
    }

    [Test]
    public async Task ReleaseReservation_RestoresQuota()
    {
        var reservation = await m_Limiter.TryReserveAttemptAsync(m_UserId);

        Assert.That(reservation, Is.Not.Null);
        Assert.That(await m_Limiter.GetRemainingAttemptsAsync(m_UserId), Is.EqualTo(4));

        await m_Limiter.ReleaseReservationAsync(m_UserId, reservation!);

        Assert.That(await m_Limiter.GetRemainingAttemptsAsync(m_UserId), Is.EqualTo(5));
    }
}
