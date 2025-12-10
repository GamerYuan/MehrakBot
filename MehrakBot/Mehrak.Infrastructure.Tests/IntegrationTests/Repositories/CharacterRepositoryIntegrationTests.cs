using Mehrak.Domain.Enums;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Mehrak.Infrastructure.Tests.IntegrationTests.Repositories;

[TestFixture]
internal class CharacterRepositoryIntegrationTests
{
    private static CharacterDbContext CreateContext() => PostgresTestHelper.Instance.CreateCharacterDbContext();

    private static CharacterRepository CreateRepository(CharacterDbContext ctx)
        => new(CreateScopeFactory(ctx), NullLogger<CharacterRepository>.Instance);

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

    [Test]
    public async Task UpsertCharactersAsync_PersistsNewNames()
    {
        const Game game = Game.HonkaiStarRail;
        var characters = new[] { $"Char_{Guid.NewGuid():N}", $"Char_{Guid.NewGuid():N}" };

        await using (var ctx = CreateContext())
        {
            var repo = CreateRepository(ctx);
            await repo.UpsertCharactersAsync(game, characters);
        }

        await using var verificationCtx = CreateContext();
        var verificationRepo = CreateRepository(verificationCtx);
        var stored = await verificationRepo.GetCharactersAsync(game);

        Assert.That(stored, Is.SupersetOf(characters));
    }

    [Test]
    public async Task UpsertCharactersAsync_AddsOnlyNewEntries()
    {
        const Game game = Game.Genshin;
        var initial = new[] { $"Amber_{Guid.NewGuid():N}", $"Lisa_{Guid.NewGuid():N}" };

        await using (var ctx = CreateContext())
        {
            var repo = CreateRepository(ctx);
            await repo.UpsertCharactersAsync(game, initial);
        }

        var existingName = initial[0];
        var newName = $"Noelle_{Guid.NewGuid():N}";

        await using (var ctx = CreateContext())
        {
            var repo = CreateRepository(ctx);
            await repo.UpsertCharactersAsync(game, [existingName, newName]);
        }

        await using var verificationCtx = CreateContext();
        var verificationRepo = CreateRepository(verificationCtx);
        var stored = await verificationRepo.GetCharactersAsync(game);

        Assert.That(stored.Count(x => x == existingName), Is.EqualTo(1));
        Assert.That(stored, Does.Contain(newName));
    }

    [Test]
    public async Task GetCharactersAsync_FiltersByGame()
    {
        var hsrName = $"HSR_{Guid.NewGuid():N}";
        var genshinName = $"GEN_{Guid.NewGuid():N}";

        await using (var ctx = CreateContext())
        {
            var repo = CreateRepository(ctx);
            await repo.UpsertCharactersAsync(Game.HonkaiStarRail, [hsrName]);
            await repo.UpsertCharactersAsync(Game.Genshin, [genshinName]);
        }

        await using var verificationCtx = CreateContext();
        var verificationRepo = CreateRepository(verificationCtx);
        var hsrCharacters = await verificationRepo.GetCharactersAsync(Game.HonkaiStarRail);

        Assert.That(hsrCharacters, Does.Contain(hsrName));
        Assert.That(hsrCharacters, Does.Not.Contain(genshinName));
    }
}
