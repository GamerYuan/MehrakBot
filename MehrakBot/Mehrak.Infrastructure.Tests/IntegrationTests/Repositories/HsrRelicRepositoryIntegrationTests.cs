using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Mehrak.Infrastructure.Tests.IntegrationTests.Repositories;

[TestFixture]
internal class HsrRelicRepositoryIntegrationTests
{
    private static RelicDbContext CreateContext() => PostgresTestHelper.Instance.CreateRelicDbContext();

    private static HsrRelicRepository CreateRepository(RelicDbContext ctx)
        => new(CreateScopeFactory(ctx), NullLogger<HsrRelicRepository>.Instance);

    private static IServiceScopeFactory CreateScopeFactory(RelicDbContext ctx)
    {
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(x => x.GetService(typeof(RelicDbContext)))
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
    public async Task AddSetName_PersistsRelicSet()
    {
        var setId = Random.Shared.Next(1000000, 9999999);
        const string setName = "Longevous Disciple";

        await using (var ctx = CreateContext())
        {
            var repo = CreateRepository(ctx);
            await repo.AddSetName(setId, setName);
        }

        await using var verificationCtx = CreateContext();
        var verificationRepo = CreateRepository(verificationCtx);
        var retrieved = await verificationRepo.GetSetName(setId);

        Assert.That(retrieved, Is.EqualTo(setName));
    }

    [Test]
    public async Task AddSetName_DoesNotOverwriteExistingEntry()
    {
        var setId = Random.Shared.Next(1000000, 9999999);

        await using (var ctx = CreateContext())
        {
            var repo = CreateRepository(ctx);
            await repo.AddSetName(setId, "Original Name");
        }

        await using (var ctx = CreateContext())
        {
            var repo = CreateRepository(ctx);
            await repo.AddSetName(setId, "New Name");
        }

        await using var verificationCtx = CreateContext();
        var verificationRepo = CreateRepository(verificationCtx);
        var retrieved = await verificationRepo.GetSetName(setId);

        Assert.That(retrieved, Is.EqualTo("Original Name"));
    }

    [Test]
    public async Task GetSetName_ReturnsEmptyStringWhenMissing()
    {
        var setId = Random.Shared.Next(1000000, 9999999);

        await using var ctx = CreateContext();
        var repo = CreateRepository(ctx);

        var retrieved = await repo.GetSetName(setId);

        Assert.That(retrieved, Is.EqualTo(string.Empty));
    }
}
