using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Character;
using Mehrak.Infrastructure.Character;
using Mehrak.Infrastructure.Character.Models;
using Mehrak.Infrastructure.Character.Services;
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
internal sealed class AliasServiceTests : IDisposable
{
    private readonly TestDbContextFactory m_DbFactory = new();
    private readonly Mock<IDatabase> m_RedisDatabase = new();
    private readonly Mock<ITransaction> m_RedisTransaction = new();
    private AliasService m_Service = null!;

    public void Dispose() => m_DbFactory.Dispose();

    private void SetupService(bool cacheRefreshSucceeds = true)
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

        m_Service = new AliasService(
            Options.Create(new RedisConfig { InstanceName = "test:" }),
            CreateScopeFactory(),
            redis.Object,
            NullLogger<AliasService>.Instance);
    }

    private IServiceScopeFactory CreateScopeFactory()
    {
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(factory => factory.CreateScope()).Returns(() =>
        {
            var context = m_DbFactory.CreateDbContext<CharacterDbContext>();
            var serviceProvider = new Mock<IServiceProvider>();
            serviceProvider.Setup(provider => provider.GetService(typeof(CharacterDbContext))).Returns(context);
            var scope = new Mock<IServiceScope>();
            scope.Setup(value => value.ServiceProvider).Returns(serviceProvider.Object);
            return scope.Object;
        });
        return scopeFactory.Object;
    }

    private CharacterDbContext CreateContext() => m_DbFactory.CreateDbContext<CharacterDbContext>();

    [Test]
    public async Task UpsertAliases_MixedCaseExistingRow_UsesCanonicalDatabaseIdentity()
    {
        SetupService();
        await using (var context = CreateContext())
        {
            context.Aliases.Add(new AliasModel
            {
                Game = Game.Genshin,
                Alias = "Raiden",
                CharacterName = "Raiden Shogun"
            });
            await context.SaveChangesAsync();
            await context.Database.ExecuteSqlRawAsync("UPDATE Aliases SET Alias = 'RAIDEN'");
        }

        await m_Service.UpsertAliases(Game.Genshin, new Dictionary<string, string>
        {
            ["  raIDen  "] = "Raiden Shogun"
        });

        var aliases = m_Service.GetAliases(Game.Genshin);
        await using var verifyContext = CreateContext();
        var rows = await verifyContext.Aliases.Where(alias => alias.Game == Game.Genshin).ToListAsync();

        Assert.Multiple(() =>
        {
            Assert.That(aliases, Has.Count.EqualTo(1));
            Assert.That(aliases["RAIDEN"], Is.EqualTo("Raiden Shogun"));
            Assert.That(rows, Has.Count.EqualTo(1));
            Assert.That(rows[0].Alias, Is.EqualTo("raiden"));
        });
    }

    [Test]
    public async Task UpsertAliases_CacheFailureAfterCommit_ThrowsCommittedSynchronizationError()
    {
        SetupService(cacheRefreshSucceeds: false);

        var exception = Assert.ThrowsAsync<CacheSynchronizationException>(() =>
            m_Service.UpsertAliases(Game.Genshin, new Dictionary<string, string>
            {
                ["Raiden"] = "Raiden Shogun"
            }));

        Assert.That(exception!.DatabaseCommitted, Is.True);
        await using var verifyContext = CreateContext();
        Assert.That(await verifyContext.Aliases.AnyAsync(alias =>
            alias.Game == Game.Genshin && alias.Alias == "raiden"), Is.True);
    }

    [Test]
    public async Task ReconcileAliases_ConflictingCaseCollision_PreservesLoserInConflictLedger()
    {
        SetupService();
        await using (var context = CreateContext())
        {
            context.Aliases.Add(new AliasModel
            {
                Game = Game.Genshin,
                Alias = "raiden",
                CharacterName = "Raiden Shogun"
            });
            await context.SaveChangesAsync();
            await context.Database.ExecuteSqlRawAsync("INSERT INTO Aliases (Game, Alias, CharacterName) VALUES (1, 'RAIDEN', 'Raiden')");
        }

        await m_Service.ReconcileAliasesAsync();

        await using var verifyContext = CreateContext();
        var aliasCount = await verifyContext.Aliases.CountAsync(alias => alias.Game == Game.Genshin);
        var conflictExists = await verifyContext.AliasConflicts.AnyAsync(conflict =>
            conflict.Alias == "raiden" && conflict.CharacterName == "Raiden" && conflict.OriginalAlias == "RAIDEN");

        Assert.Multiple(() =>
        {
            Assert.That(aliasCount, Is.EqualTo(1));
            Assert.That(conflictExists, Is.True);
        });
    }
}
