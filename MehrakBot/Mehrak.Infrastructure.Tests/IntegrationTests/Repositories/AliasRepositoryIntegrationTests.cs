using Mehrak.Domain.Enums;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Mehrak.Infrastructure.Tests.IntegrationTests.Repositories;

[TestFixture]
internal class AliasRepositoryIntegrationTests
{
    private static CharacterDbContext CreateContext() => PostgresTestHelper.Instance.CreateCharacterDbContext();

    private static AliasRepository CreateRepository(CharacterDbContext ctx)
        => new(CreateScopeFactory(ctx), NullLogger<AliasRepository>.Instance);

    private static IServiceScopeFactory CreateScopeFactory(CharacterDbContext ctx)
    {
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(x => x.GetService(typeof(CharacterDbContext)))
            .Returns(ctx);

        var serviceScope = new Mock<IServiceScope>();
        serviceScope.Setup(x => x.ServiceProvider).Returns(serviceProvider.Object);

        var serviceScopeFactory = new Mock<IServiceScopeFactory>();
        serviceScopeFactory
            .Setup(x => x.CreateScope())
            .Returns(serviceScope.Object);

        serviceProvider
            .Setup(x => x.GetService(typeof(IServiceScopeFactory)))
            .Returns(serviceScopeFactory.Object);

        return serviceScopeFactory.Object;
    }

    private static string UniqueAlias(string prefix)
    {
        var guid = Guid.NewGuid().ToString("N");
        return (prefix + guid)[..Math.Min(20, prefix.Length + guid.Length)];
    }

    [Test]
    public async Task UpsertAliasAsync_PersistsNewAliases()
    {
        var aliasKey = UniqueAlias("hsr");
        const string characterName = "Seele";

        await using (var ctx = CreateContext())
        {
            var repo = CreateRepository(ctx);
            await repo.UpsertAliasAsync(Game.HonkaiStarRail, new Dictionary<string, string>
            {
                { aliasKey, characterName }
            });
        }

        await using var verificationCtx = CreateContext();
        var verificationRepo = CreateRepository(verificationCtx);
        var aliases = await verificationRepo.GetAliasesAsync(Game.HonkaiStarRail);

        Assert.That(aliases.TryGetValue(aliasKey, out var storedName), Is.True);
        Assert.That(storedName, Is.EqualTo(characterName));
    }

    [Test]
    public async Task UpsertAliasAsync_UpdatesExistingAlias()
    {
        var aliasKey = UniqueAlias("update");

        await using (var ctx = CreateContext())
        {
            var repo = CreateRepository(ctx);
            await repo.UpsertAliasAsync(Game.Genshin, new Dictionary<string, string>
            {
                { aliasKey, "OldName" }
            });
        }

        await using (var ctx = CreateContext())
        {
            var repo = CreateRepository(ctx);
            await repo.UpsertAliasAsync(Game.Genshin, new Dictionary<string, string>
            {
                { aliasKey, "NewName" }
            });
        }

        await using var verificationCtx = CreateContext();
        var verificationRepo = CreateRepository(verificationCtx);
        var aliases = await verificationRepo.GetAliasesAsync(Game.Genshin);

        Assert.That(aliases[aliasKey], Is.EqualTo("NewName"));
    }

    [Test]
    public async Task GetAliasesAsync_FiltersByGame()
    {
        var hsrAlias = UniqueAlias("hsr");
        var genshinAlias = UniqueAlias("gen");

        await using (var ctx = CreateContext())
        {
            var repo = CreateRepository(ctx);
            await repo.UpsertAliasAsync(Game.HonkaiStarRail, new Dictionary<string, string>
            {
                { hsrAlias, "HSR Character" }
            });
            await repo.UpsertAliasAsync(Game.Genshin, new Dictionary<string, string>
            {
                { genshinAlias, "Genshin Character" }
            });
        }

        await using var verificationCtx = CreateContext();
        var verificationRepo = CreateRepository(verificationCtx);
        var hsrAliases = await verificationRepo.GetAliasesAsync(Game.HonkaiStarRail);

        Assert.That(hsrAliases.ContainsKey(hsrAlias), Is.True);
        Assert.That(hsrAliases.ContainsKey(genshinAlias), Is.False);
    }
}
