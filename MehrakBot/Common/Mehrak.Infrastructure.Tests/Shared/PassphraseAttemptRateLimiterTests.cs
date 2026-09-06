using Mehrak.Infrastructure.Shared;
using Moq;
using StackExchange.Redis;

namespace Mehrak.Infrastructure.Tests.Shared;

[TestFixture]
[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class PassphraseAttemptRateLimiterTests
{
    private Mock<IConnectionMultiplexer> m_MockRedis;
    private Mock<IDatabase> m_MockDatabase;
    private PassphraseAttemptRateLimiter m_Limiter;

    [SetUp]
    public void SetUp()
    {
        m_MockRedis = new Mock<IConnectionMultiplexer>();
        m_MockDatabase = new Mock<IDatabase>();
        m_MockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
            .Returns(m_MockDatabase.Object);
        m_Limiter = new PassphraseAttemptRateLimiter(m_MockRedis.Object);
    }

    [Test]
    public async Task IsBlockedAsync_UnderLimit_ReturnsFalse()
    {
        // Arrange
        const ulong userId = 123456789;
        m_MockDatabase.Setup(d => d.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create(0, ResultType.Integer));

        // Act
        var result = await m_Limiter.IsBlockedAsync(userId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task IsBlockedAsync_AtLimit_ReturnsTrue()
    {
        // Arrange
        const ulong userId = 123456789;
        m_MockDatabase.Setup(d => d.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create(1, ResultType.Integer));

        // Act
        var result = await m_Limiter.IsBlockedAsync(userId);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task RecordFailureAsync_CallsScriptEvaluate()
    {
        // Arrange
        const ulong userId = 123456789;
        m_MockDatabase.Setup(d => d.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create(1, ResultType.Integer));

        // Act
        await m_Limiter.RecordFailureAsync(userId);

        // Assert
        m_MockDatabase.Verify(d => d.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Test]
    public async Task TryReserveAttemptAsync_WhenAllowed_ReturnsReservationId()
    {
        // Arrange
        const ulong userId = 123456789;
        m_MockDatabase.Setup(d => d.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create("1700000000000:abc123", ResultType.BulkString));

        // Act
        var result = await m_Limiter.TryReserveAttemptAsync(userId);

        // Assert
        Assert.That(result, Is.EqualTo("1700000000000:abc123"));
    }

    [Test]
    public async Task TryReserveAttemptAsync_WhenBlocked_ReturnsNull()
    {
        // Arrange
        const ulong userId = 123456789;
        m_MockDatabase.Setup(d => d.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create(RedisValue.Null, ResultType.BulkString));

        // Act
        var result = await m_Limiter.TryReserveAttemptAsync(userId);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task RecordFailureAsync_WithFixedTimestamp_UsesUniqueMembers()
    {
        // Arrange: a fixed timestamp proves same-millisecond events are
        // counted individually instead of overwriting one sorted-set member.
        const ulong userId = 123456789;
        var fixedTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        m_Limiter.UtcNowProvider = () => fixedTime;
        var members = new List<string>();
        m_MockDatabase.Setup(d => d.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CommandFlags>()))
            .Callback<string, RedisKey[], RedisValue[], CommandFlags>((_, _, values, _) => members.Add(values[2].ToString()))
            .ReturnsAsync(RedisResult.Create(1, ResultType.Integer));

        // Act
        await m_Limiter.RecordFailureAsync(userId);
        await m_Limiter.RecordFailureAsync(userId);

        // Assert
        Assert.That(members, Has.Count.EqualTo(2));
        Assert.That(members[0], Is.Not.EqualTo(members[1]));
        Assert.That(members, Has.All.StartsWith("1767225600000:"));
    }

    [Test]
    public async Task TryReserveAttemptAsync_WithFixedTimestamp_UsesUniqueMembers()
    {
        // Arrange
        const ulong userId = 123456789;
        var fixedTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        m_Limiter.UtcNowProvider = () => fixedTime;
        var members = new List<string>();
        var results = new Queue<RedisResult>(new[]
        {
            RedisResult.Create("m:1", ResultType.BulkString),
            RedisResult.Create("m:2", ResultType.BulkString)
        });
        m_MockDatabase.Setup(d => d.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CommandFlags>()))
            .Callback<string, RedisKey[], RedisValue[], CommandFlags>((_, _, values, _) => members.Add(values[2].ToString()))
            .Returns(() => Task.FromResult(results.Dequeue()));

        // Act
        var first = await m_Limiter.TryReserveAttemptAsync(userId);
        var second = await m_Limiter.TryReserveAttemptAsync(userId);

        // Assert
        Assert.That(first, Is.Not.Null);
        Assert.That(second, Is.Not.Null);
        Assert.That(first, Is.Not.EqualTo(second));
    }

    [Test]
    public async Task TryReserveAttemptAsync_ReservesBeforeWork_SingleAtomicScript()
    {
        // Arrange: one Lua invocation both checks the quota and records the
        // attempt, so concurrent callers cannot all slip past the check.
        const ulong userId = 123456789;
        string? capturedScript = null;
        m_MockDatabase.Setup(d => d.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CommandFlags>()))
            .Callback<string, RedisKey[], RedisValue[], CommandFlags>((script, _, _, _) => capturedScript = script)
            .ReturnsAsync(RedisResult.Create("m:1", ResultType.BulkString));

        // Act
        await m_Limiter.TryReserveAttemptAsync(userId);

        // Assert
        m_MockDatabase.Verify(d => d.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CommandFlags>()), Times.Once);
        Assert.That(capturedScript, Does.Contain("ZCARD"));
        Assert.That(capturedScript, Does.Contain("ZADD"));
    }

    [Test]
    public async Task ReleaseReservationAsync_RemovesMember()
    {
        // Arrange
        const ulong userId = 123456789;
        const string reservation = "1700000000000:abc123";
        string? capturedScript = null;
        RedisValue[]? capturedValues = null;
        m_MockDatabase.Setup(d => d.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CommandFlags>()))
            .Callback<string, RedisKey[], RedisValue[], CommandFlags>((script, _, values, _) =>
            {
                capturedScript = script;
                capturedValues = values;
            })
            .ReturnsAsync(RedisResult.Create(1, ResultType.Integer));

        // Act
        await m_Limiter.ReleaseReservationAsync(userId, reservation);

        // Assert
        Assert.That(capturedScript, Does.Contain("ZREM"));
        Assert.That(capturedValues, Is.Not.Null);
        Assert.That(capturedValues![0].ToString(), Is.EqualTo(reservation));
    }

    [Test]
    public async Task GetRemainingAttemptsAsync_ReturnsCorrectCount()
    {
        // Arrange
        const ulong userId = 123456789;
        m_MockDatabase.Setup(d => d.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create(3, ResultType.Integer));

        // Act
        var result = await m_Limiter.GetRemainingAttemptsAsync(userId);

        // Assert
        Assert.That(result, Is.EqualTo(2));
    }
}
