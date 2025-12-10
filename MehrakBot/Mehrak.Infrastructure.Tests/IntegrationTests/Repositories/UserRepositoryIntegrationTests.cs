using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Mehrak.Infrastructure.Tests.IntegrationTests.Repositories;

[TestFixture]
internal class UserRepositoryIntegrationTests
{
    private static UserDbContext CreateContext() => PostgresTestHelper.Instance.CreateUserDbContext();

    private static UserRepository CreateRepository(UserDbContext ctx)
        => new(CreateScopeFactory(ctx), NullLogger<UserRepository>.Instance);

    private static IServiceScopeFactory CreateScopeFactory(UserDbContext ctx)
    {
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(x => x.GetService(typeof(UserDbContext)))
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
    public async Task CreateOrUpdateUserAsync_InsertsAndRetrievesUser()
    {
        await using var ctx = CreateContext();
        var repo = CreateRepository(ctx);

        var userId = 1001UL;
        var dto = new UserDto
        {
            Id = userId,
            Timestamp = System.DateTime.UtcNow,
            Profiles =
            [
                new UserProfileDto
                {
                    ProfileId = 1,
                    LtUid = 9999UL,
                    LToken = "token-1",
                    LastCheckIn = null,
                    GameUids = new Dictionary<Game, Dictionary<string, string>>
                    {
                        { Game.HonkaiStarRail, new Dictionary<string, string> { { "America", "8001" } } }
                    },
                    LastUsedRegions = new Dictionary<Game, string>
                    {
                        { Game.HonkaiStarRail, "America" }
                    }
                }
            ]
        };

        await repo.CreateOrUpdateUserAsync(dto);

        var fetched = await repo.GetUserAsync(userId);
        Assert.That(fetched, Is.Not.Null);
        var profiles = fetched!.Profiles!.ToList();
        Assert.That(profiles.Count, Is.EqualTo(1));
        var p = profiles[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(p.ProfileId, Is.EqualTo(1));
            Assert.That(p.LtUid, Is.EqualTo(9999UL));
            Assert.That(p.LToken, Is.EqualTo("token-1"));
            Assert.That(p.GameUids![Game.HonkaiStarRail]["America"], Is.EqualTo("8001"));
            Assert.That(p.LastUsedRegions![Game.HonkaiStarRail], Is.EqualTo("America"));
        }
    }

    [Test]
    public async Task CreateOrUpdateUserAsync_ReplacesProfilesOnUpdate()
    {
        await using var ctx = CreateContext();
        var repo = CreateRepository(ctx);

        var userId = 2002UL;

        // Seed with one profile
        await repo.CreateOrUpdateUserAsync(new UserDto
        {
            Id = userId,
            Timestamp = System.DateTime.UtcNow,
            Profiles =
            [
                new UserProfileDto
                {
                    ProfileId = 1,
                    LtUid = 1,
                    LToken = "a",
                    LastCheckIn = null
                }
            ]
        });

        // Update with two different profiles (should replace previous set)
        await repo.CreateOrUpdateUserAsync(new UserDto
        {
            Id = userId,
            Timestamp = System.DateTime.UtcNow,
            Profiles =
            [
                new UserProfileDto { ProfileId = 2, LtUid = 2, LToken = "b", LastCheckIn = null },
                new UserProfileDto { ProfileId = 3, LtUid = 3, LToken = "c", LastCheckIn = null }
            ]
        });

        var fetched = await repo.GetUserAsync(userId);
        Assert.That(fetched, Is.Not.Null);
        var fetchedProfiles = fetched!.Profiles!.ToList();
        Assert.That(fetchedProfiles, Has.Count.EqualTo(2));
        var ids = fetchedProfiles.Select(x => x.ProfileId).ToHashSet();
        Assert.That(ids.SetEquals([2U, 3U]), Is.True);
    }

    [Test]
    public async Task DeleteUserAsync_RemovesUser()
    {
        await using var ctx = CreateContext();
        var repo = CreateRepository(ctx);

        var userId = 3003UL;
        await repo.CreateOrUpdateUserAsync(new UserDto
        {
            Id = userId,
            Timestamp = System.DateTime.UtcNow,
            Profiles =
            [
                new UserProfileDto { ProfileId = 1, LtUid = 11UL, LToken = "x", LastCheckIn = null }
            ]
        });

        var existedBeforeDelete = await repo.GetUserAsync(userId);
        Assert.That(existedBeforeDelete, Is.Not.Null);

        var deleted = await repo.DeleteUserAsync(userId);
        Assert.That(deleted, Is.True);

        var afterDelete = await repo.GetUserAsync(userId);
        Assert.That(afterDelete, Is.Null);
    }
}
