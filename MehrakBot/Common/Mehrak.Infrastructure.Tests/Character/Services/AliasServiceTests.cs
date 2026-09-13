using Mehrak.Domain.Shared.Enums;
using Mehrak.Infrastructure.Character;
using Mehrak.Infrastructure.Character.Models;
using Mehrak.Infrastructure.Character.Services;
using Mehrak.Infrastructure.Tests.TestUtils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Mehrak.Infrastructure.Tests.Character.Services;

[TestFixture]
[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
internal sealed class AliasServiceTests : IDisposable
{
    private readonly TestDbContextFactory m_DbFactory = new();
    private AliasService m_Service = null!;

    public void Dispose() => m_DbFactory.Dispose();

    private void SetupService()
    {
        m_Service = new AliasService(CreateScopeFactory(), NullLogger<AliasService>.Instance);
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
    public async Task UpsertAliases_MixedCaseRequest_UpdatesCanonicalDatabaseRow()
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
    public async Task UpsertAliases_DatabaseOnlyService_NormalizesNewAlias()
    {
        SetupService();

        await m_Service.UpsertAliases(Game.Genshin, new Dictionary<string, string>
        {
            ["Raiden"] = "Raiden Shogun"
        });

        await using var verifyContext = CreateContext();
        Assert.That(await verifyContext.Aliases.AnyAsync(alias =>
            alias.Game == Game.Genshin && alias.Alias == "raiden"), Is.True);
    }

    [Test]
    public async Task DeleteAlias_MixedCaseRequest_DeletesCanonicalRow()
    {
        SetupService();
        await m_Service.UpsertAliases(Game.Genshin, new() { ["Raiden"] = "Raiden Shogun" });
        await m_Service.DeleteAlias(Game.Genshin, "  RAIDEN\r\n");
        Assert.That(m_Service.GetAliases(Game.Genshin), Is.Empty);
    }

    [Test]
    public void UpsertAliases_ConflictingNormalizedTargets_RejectsBeforeWriting()
    {
        SetupService();
        Assert.ThrowsAsync<InvalidOperationException>(() => m_Service.UpsertAliases(Game.Genshin,
            new() { ["Raiden"] = "Raiden Shogun", [" RAIDEN "] = "Raiden" }));
        Assert.That(m_Service.GetAliases(Game.Genshin), Is.Empty);
    }
}
