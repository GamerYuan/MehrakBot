using Mehrak.Domain.Character;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Infrastructure.Character;
using Mehrak.Infrastructure.Character.Services;
using Mehrak.Infrastructure.Character.Models;
using Mehrak.Infrastructure.Shared.Config;
using Mehrak.Infrastructure.Tests.TestUtils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;

namespace Mehrak.Infrastructure.Tests.Character.Services;

[TestFixture]
[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
internal sealed class CharacterCacheServiceTests : IDisposable
{
    private readonly TestDbContextFactory m_DbFactory = new();
    private readonly Mock<IDatabase> m_RedisDatabase = new();
    private readonly Mock<ITransaction> m_RedisTransaction = new();

    public void Dispose() => m_DbFactory.Dispose();

    private CharacterCacheService CreateService(bool cacheRefreshSucceeds)
    {
        m_RedisTransaction
            .Setup(transaction => transaction.ExecuteAsync(It.IsAny<CommandFlags>()))
            .ReturnsAsync(cacheRefreshSucceeds);
        m_RedisDatabase
            .Setup(database => database.CreateTransaction(It.IsAny<object>()))
            .Returns(m_RedisTransaction.Object);
        m_RedisDatabase
            .Setup(database => database.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(connection => connection.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(m_RedisDatabase.Object);

        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(factory => factory.CreateScope()).Returns(() =>
        {
            var context = m_DbFactory.CreateDbContext<CharacterDbContext>();
            var provider = new Mock<IServiceProvider>();
            provider.Setup(value => value.GetService(typeof(CharacterDbContext))).Returns(context);
            var scope = new Mock<IServiceScope>();
            scope.Setup(value => value.ServiceProvider).Returns(provider.Object);
            return scope.Object;
        });

        return new CharacterCacheService(
            Options.Create(new RedisConfig { InstanceName = "test:" }),
            scopeFactory.Object,
            redis.Object,
            NullLogger<CharacterCacheService>.Instance);
    }

    [Test]
    public async Task UpsertCharacters_CacheFailureAfterCommit_ThrowsCommittedSynchronizationError()
    {
        var service = CreateService(cacheRefreshSucceeds: false);

        var exception = Assert.ThrowsAsync<CacheSynchronizationException>(() =>
            service.UpsertCharacters(Game.Genshin, ["Raiden"]));

        Assert.That(exception!.DatabaseCommitted, Is.True);
        await using var context = m_DbFactory.CreateDbContext<CharacterDbContext>();
        Assert.That(await context.Characters.AnyAsync(character =>
            character.Game == Game.Genshin && character.Name == "Raiden"), Is.True);

        Assert.That(service.GetCharacters(Game.Genshin), Is.EqualTo(["Raiden"]));
    }
}
