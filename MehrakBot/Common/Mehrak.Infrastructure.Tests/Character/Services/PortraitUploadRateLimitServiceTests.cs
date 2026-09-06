using Mehrak.Infrastructure.Character.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;

namespace Mehrak.Infrastructure.Tests.Character.Services;

/// <summary>
/// Every upload event gets a unique sorted-set member while the score stays a timestamp, and the quota check plus
/// reservation happen in a single atomic Lua script shared across instances via Redis. </summary>
[TestFixture]
[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
internal class PortraitUploadRateLimitServiceTests
{
    private Mock<IConnectionMultiplexer> m_MockRedis = null!;
    private Mock<IDatabase> m_MockDatabase = null!;
    private PortraitUploadRateLimitService m_Limiter = null!;

    [SetUp]
    public void SetUp()
    {
        m_MockRedis = new Mock<IConnectionMultiplexer>();
        m_MockDatabase = new Mock<IDatabase>();
        m_MockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
            .Returns(m_MockDatabase.Object);
        m_Limiter = new PortraitUploadRateLimitService(m_MockRedis.Object, NullLogger<PortraitUploadRateLimitService>.Instance);
    }

    [Test]
    public async Task IsAllowedAsync_UnderLimit_ReturnsTrue()
    {
        // Arrange
        m_MockDatabase.Setup(d => d.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create(1, ResultType.Integer));

        // Act
        var result = await m_Limiter.IsAllowedAsync(123456789L);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task IsAllowedAsync_AtLimit_ReturnsFalse()
    {
        // Arrange
        m_MockDatabase.Setup(d => d.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create(0, ResultType.Integer));

        // Act
        var result = await m_Limiter.IsAllowedAsync(123456789L);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task IsAllowedAsync_WithFixedTimestamp_UsesUniqueMembers()
    {
        // Arrange: a fixed timestamp proves same-millisecond uploads are
        // counted individually instead of overwriting one sorted-set member.
        var fixedTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        m_Limiter.UtcNowProvider = () => fixedTime;
        var members = new List<string>();
        m_MockDatabase.Setup(d => d.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CommandFlags>()))
            .Callback<string, RedisKey[], RedisValue[], CommandFlags>((_, _, values, _) => members.Add(values[3].ToString()))
            .ReturnsAsync(RedisResult.Create(1, ResultType.Integer));

        // Act
        await m_Limiter.IsAllowedAsync(123456789L);
        await m_Limiter.IsAllowedAsync(123456789L);

        // Assert
        Assert.That(members, Has.Count.EqualTo(2));
        Assert.That(members[0], Is.Not.EqualTo(members[1]));
        Assert.That(members, Has.All.StartsWith("1767225600000:"));
    }

    [Test]
    public async Task IsAllowedAsync_CheckAndReserveInSingleAtomicScript()
    {
        // Arrange
        string? capturedScript = null;
        m_MockDatabase.Setup(d => d.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CommandFlags>()))
            .Callback<string, RedisKey[], RedisValue[], CommandFlags>((script, _, _, _) => capturedScript = script)
            .ReturnsAsync(RedisResult.Create(1, ResultType.Integer));

        // Act
        await m_Limiter.IsAllowedAsync(123456789L);

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
    public async Task GetRemainingAsync_ReturnsCorrectCount()
    {
        // Arrange
        m_MockDatabase.Setup(d => d.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create(3, ResultType.Integer));

        // Act
        var result = await m_Limiter.GetRemainingAsync(123456789L);

        // Assert
        Assert.That(result, Is.EqualTo(2));
    }
}


