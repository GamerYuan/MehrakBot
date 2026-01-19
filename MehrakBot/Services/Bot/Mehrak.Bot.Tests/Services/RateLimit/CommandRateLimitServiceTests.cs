using Mehrak.Bot.Services.RateLimit;
using Mehrak.Infrastructure.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;

namespace Mehrak.Bot.Tests.Services.RateLimit;

[TestFixture]
public class CommandRateLimitServiceTests
{
    private Mock<IConnectionMultiplexer> m_MockRedisConnection;
    private Mock<IDatabase> m_MockDatabase;
    private Mock<IOptions<RedisConfig>> m_MockRedisConfig;
    private Mock<IOptions<RateLimiterConfig>> m_MockRateLimiterConfig;
    private Mock<ILogger<CommandRateLimitService>> m_MockLogger;
    private CommandRateLimitService m_Service;

    [SetUp]
    public void Setup()
    {
        m_MockRedisConnection = new Mock<IConnectionMultiplexer>();
        m_MockDatabase = new Mock<IDatabase>();
        m_MockRedisConfig = new Mock<IOptions<RedisConfig>>();
        m_MockRateLimiterConfig = new Mock<IOptions<RateLimiterConfig>>();
        m_MockLogger = new Mock<ILogger<CommandRateLimitService>>();

        m_MockRedisConnection.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(m_MockDatabase.Object);

        // Default configs
        m_MockRedisConfig.Setup(x => x.Value).Returns(new RedisConfig { InstanceName = "TestInstance:" });
        m_MockRateLimiterConfig.Setup(x => x.Value).Returns(new RateLimiterConfig
        {
            LeakInterval = TimeSpan.FromSeconds(1),
            Capacity = 5
        });

        m_Service = new CommandRateLimitService(
            m_MockRedisConfig.Object,
            m_MockRateLimiterConfig.Object,
            m_MockRedisConnection.Object,
            m_MockLogger.Object);
    }

    [Test]
    public async Task IsAllowedAsync_WithValidUserId_CallsRedisScriptEvaluate()
    {
        // Arrange
        ulong userId = 123456789;
        var expectedKey = "TestInstance:cmd_rate_limit:123456789";

        // Mock ScriptEvaluateAsync to return 1 (Allowed)
        m_MockDatabase.Setup(x => x.ScriptEvaluateAsync(
            It.IsAny<LuaScript>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create(1));

        // Act
        var result = await m_Service.IsAllowedAsync(userId);

        // Assert
        Assert.That(result, Is.True);

        m_MockDatabase.Verify(x => x.ScriptEvaluateAsync(
            It.IsAny<LuaScript>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<CommandFlags>()), Times.Once);

        m_MockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"User {userId} is allowed: True")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public async Task IsAllowedAsync_WhenRedisReturns0_ReturnsFalse()
    {
        // Arrange
        ulong userId = 123456789;

        // Mock ScriptEvaluateAsync to return 0 (Not allowed)
        m_MockDatabase.Setup(x => x.ScriptEvaluateAsync(
            It.IsAny<LuaScript>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create(0));

        // Act
        var result = await m_Service.IsAllowedAsync(userId);

        // Assert
        Assert.That(result, Is.False);

        m_MockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"User {userId} is allowed: False")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public async Task IsAllowedAsync_CalculatesCorrectParameters()
    {
        // Arrange
        ulong userId = 999;
        var leakInterval = TimeSpan.FromSeconds(2);
        var capacity = 10;

        m_MockRateLimiterConfig.Setup(x => x.Value).Returns(new RateLimiterConfig
        {
            LeakInterval = leakInterval,
            Capacity = capacity
        });

        // Re-create service to pick up new config
        m_Service = new CommandRateLimitService(
            m_MockRedisConfig.Object,
            m_MockRateLimiterConfig.Object,
            m_MockRedisConnection.Object,
            m_MockLogger.Object);

        var expectedLeakMs = (long)leakInterval.TotalMilliseconds; // 2000
        var expectedBurstOffsetMs = expectedLeakMs * capacity; // 20000

        m_MockDatabase.Setup(x => x.ScriptEvaluateAsync(
            It.IsAny<LuaScript>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create(1));

        // Act
        await m_Service.IsAllowedAsync(userId);

        // Assert
        m_MockDatabase.Verify(x => x.ScriptEvaluateAsync(
            It.IsAny<LuaScript>(),
            It.Is<object>(obj =>
                (long)obj.GetType().GetProperty("inputLeak")!.GetValue(obj)! == expectedLeakMs &&
                (long)obj.GetType().GetProperty("inputBurst")!.GetValue(obj)! == expectedBurstOffsetMs),
            It.IsAny<CommandFlags>()), Times.Once);
    }
}
