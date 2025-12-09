using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mehrak.Infrastructure.Tests.IntegrationTests.Repositories;

[TestFixture]
internal class HsrRelicRepositoryIntegrationTests
{
    private static RelicDbContext CreateContext() => PostgresTestHelper.Instance.CreateRelicDbContext();

    private static HsrRelicRepository CreateRepository(RelicDbContext ctx)
        => new(ctx, NullLogger<HsrRelicRepository>.Instance);

    [Test]
    public async Task AddSetName_PersistsRelicSet()
    {
        const int setId = 4101;
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
        const int setId = 4102;

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
        const int setId = 987654;

        await using var ctx = CreateContext();
        var repo = CreateRepository(ctx);

        var retrieved = await repo.GetSetName(setId);

        Assert.That(retrieved, Is.EqualTo(string.Empty));
    }
}
