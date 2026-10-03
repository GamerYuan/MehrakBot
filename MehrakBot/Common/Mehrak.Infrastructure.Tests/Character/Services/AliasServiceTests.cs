using System.Text.Json;
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

    [TestCase("raiden")]
    [TestCase("Raiden")]
    public async Task UpsertAliases_MixedCaseRequest_UpdatesExistingRowAndPreservesSpelling(string storedAlias)
    {
        SetupService();
        await using (var context = CreateContext())
        {
            context.Aliases.Add(new AliasModel
            {
                Game = Game.Genshin,
                Alias = storedAlias,
                CharacterName = "Raiden"
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
            Assert.That(rows[0].Alias, Is.EqualTo(storedAlias));
            Assert.That(rows[0].CharacterName, Is.EqualTo("Raiden Shogun"));
            Assert.That(aliases.Keys.Single(), Is.EqualTo(storedAlias));
        });
    }

    [TestCase("Raiden")]
    [TestCase("TB")]
    [TestCase("Älias")]
    public async Task UpsertAliases_NewAlias_PreservesCapitalizationAndCleansWhitespace(string alias)
    {
        SetupService();

        await m_Service.UpsertAliases(Game.Genshin, new Dictionary<string, string>
        {
            [$"  {alias}\r\n  "] = "Raiden Shogun"
        });

        await using var verifyContext = CreateContext();
        var row = await verifyContext.Aliases.SingleAsync(entry => entry.Game == Game.Genshin);
        var aliases = m_Service.GetAliases(Game.Genshin);
        Assert.Multiple(() =>
        {
            Assert.That(row.Alias, Is.EqualTo(alias));
            Assert.That(aliases.Keys.Single(), Is.EqualTo(alias));
            Assert.That(aliases[alias.ToLowerInvariant()], Is.EqualTo("Raiden Shogun"));
        });
    }

    [Test]
    public async Task DeleteAlias_MixedCaseRequest_DeletesOnlyMatchingGameAlias()
    {
        SetupService();
        await m_Service.UpsertAliases(Game.Genshin, new() { ["Raiden"] = "Raiden Shogun" });
        await m_Service.UpsertAliases(Game.HonkaiStarRail, new() { ["Raiden"] = "Acheron" });
        await m_Service.DeleteAlias(Game.Genshin, "  RAIDEN\r\n");
        Assert.Multiple(() =>
        {
            Assert.That(m_Service.GetAliases(Game.Genshin), Is.Empty);
            Assert.That(m_Service.GetAliases(Game.HonkaiStarRail)["RAIDEN"], Is.EqualTo("Acheron"));
        });
    }

    [Test]
    public async Task UpsertAliases_DuplicateCaseVariants_PreservesFirstSpelling()
    {
        SetupService();
        await m_Service.UpsertAliases(Game.Genshin,
            new() { ["Raiden"] = "Raiden Shogun", [" RAIDEN "] = "Raiden Shogun" });

        await using var context = CreateContext();
        var row = await context.Aliases.SingleAsync();
        Assert.That(row.Alias, Is.EqualTo("Raiden"));
    }

    [Test]
    public async Task InitializeAliases_JsonInput_PreservesCapitalization()
    {
        SetupService();
        var assetsPath = Path.Combine(Path.GetTempPath(), $"mehrak-alias-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(assetsPath);
        try
        {
            var json = JsonSerializer.Serialize(new AliasJsonModel
            {
                Game = Game.Genshin,
                Aliases = [new AliasEntry { Name = "Raiden Shogun", Alias = ["  Ei\r\n", "RAIDEN"] }]
            });
            await File.WriteAllTextAsync(Path.Combine(assetsPath, "aliases.json"), json);
            var initializer = new AliasInitializationService(
                NullLogger<AliasInitializationService>.Instance, m_Service, assetsPath);

            await initializer.StartAsync(TestContext.CurrentContext.CancellationToken);

            var aliases = m_Service.GetAliases(Game.Genshin);
            Assert.Multiple(() =>
            {
                Assert.That(aliases.Keys, Is.EquivalentTo(new[] { "Ei", "RAIDEN" }));
                Assert.That(aliases["ei"], Is.EqualTo("Raiden Shogun"));
            });
        }
        finally
        {
            Directory.Delete(assetsPath, true);
        }
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
