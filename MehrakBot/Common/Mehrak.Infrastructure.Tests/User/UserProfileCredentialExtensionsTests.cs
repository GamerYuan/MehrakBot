using Mehrak.Infrastructure.Tests.TestUtils;
using Mehrak.Infrastructure.User;
using Mehrak.Infrastructure.User.Extensions;
using Mehrak.Infrastructure.User.Models;
using Microsoft.EntityFrameworkCore;

namespace Mehrak.Infrastructure.Tests.User;

[TestFixture]
public class UserProfileCredentialExtensionsTests
{
    private TestDbContextFactory m_DbFactory = null!;

    [SetUp]
    public void SetUp()
    {
        m_DbFactory = new TestDbContextFactory();
        using var context = m_DbFactory.CreateDbContext<UserDbContext>();
        context.Users.Add(CreateUser());
        context.SaveChanges();
    }

    [TearDown]
    public void TearDown() => m_DbFactory.Dispose();

    [Test]
    public async Task TryCompareAndSwapLTokenAsync_ConcurrentRotationIsNotOverwritten()
    {
        await using var loginContext = m_DbFactory.CreateDbContext<UserDbContext>();
        await using var rotationContext = m_DbFactory.CreateDbContext<UserDbContext>();

        var snapshot = await loginContext.UserProfiles
            .AsNoTracking()
            .SingleAsync(profile => profile.ProfileId == 1);
        var rotation = await rotationContext.UserProfiles.SingleAsync(profile => profile.ProfileId == 1);
        rotation.LToken = "rotated-cipher";
        rotation.LastCheckIn = new DateTime(2026, 9, 7, 1, 2, 3, DateTimeKind.Utc);
        await rotationContext.SaveChangesAsync();

        var upgraded = await loginContext.TryCompareAndSwapLTokenAsync(
            snapshot.Id, snapshot.LToken, "legacy-upgrade-cipher");

        var stored = await ReadProfileAsync();
        Assert.Multiple(() =>
        {
            Assert.That(upgraded, Is.False);
            Assert.That(stored.LToken, Is.EqualTo("rotated-cipher"));
            Assert.That(stored.LastCheckIn, Is.EqualTo(rotation.LastCheckIn));
        });
    }

    [Test]
    public async Task TryCompareAndSwapLTokenAsync_ConcurrentLegacyUpgradesOnlyOneWins()
    {
        await using var firstLoginContext = m_DbFactory.CreateDbContext<UserDbContext>();
        await using var secondLoginContext = m_DbFactory.CreateDbContext<UserDbContext>();

        var firstSnapshot = await firstLoginContext.UserProfiles
            .AsNoTracking()
            .SingleAsync(profile => profile.ProfileId == 1);
        var secondSnapshot = await secondLoginContext.UserProfiles
            .AsNoTracking()
            .SingleAsync(profile => profile.ProfileId == 1);

        var results = await Task.WhenAll(
            Task.Run(() => firstLoginContext.TryCompareAndSwapLTokenAsync(
                firstSnapshot.Id, firstSnapshot.LToken, "upgrade-one")),
            Task.Run(() => secondLoginContext.TryCompareAndSwapLTokenAsync(
                secondSnapshot.Id, secondSnapshot.LToken, "upgrade-two")));

        var stored = await ReadProfileAsync();
        Assert.Multiple(() =>
        {
            Assert.That(results.Count(result => result), Is.EqualTo(1));
            Assert.That(stored.LToken, Is.AnyOf("upgrade-one", "upgrade-two"));
        });
    }

    private async Task<UserProfileModel> ReadProfileAsync()
    {
        await using var context = m_DbFactory.CreateDbContext<UserDbContext>();
        return await context.UserProfiles
            .AsNoTracking()
            .SingleAsync(profile => profile.ProfileId == 1);
    }

    private static UserModel CreateUser()
    {
        var user = new UserModel
        {
            Id = 100,
            Timestamp = DateTime.UtcNow,
            Profiles =
            [
                new UserProfileModel
                {
                    ProfileId = 1,
                    LtUid = 111,
                    LToken = "cipher-one"
                },
                new UserProfileModel
                {
                    ProfileId = 2,
                    LtUid = 222,
                    LToken = "cipher-two"
                },
                new UserProfileModel
                {
                    ProfileId = 3,
                    LtUid = 333,
                    LToken = "cipher-three"
                }
            ]
        };

        foreach (var profile in user.Profiles)
            profile.UserId = user.Id;

        return user;
    }
}
