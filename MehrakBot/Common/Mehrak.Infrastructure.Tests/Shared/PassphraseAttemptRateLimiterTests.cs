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
