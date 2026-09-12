using Mehrak.Infrastructure.Tests.TestUtils;
using Mehrak.Infrastructure.User;
using Mehrak.Infrastructure.User.Extensions;
using Mehrak.Infrastructure.User.Models;
using Microsoft.EntityFrameworkCore;

namespace Mehrak.Infrastructure.Tests.User;

[TestFixture]
public class UserProfileDeletionExtensionsTests
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
    public async Task DeleteAndReindexProfilesAsync_OnlyChangesIdsAndPreservesConcurrentProfileUpdates()
    {
        await using var deleteContext = m_DbFactory.CreateDbContext<UserDbContext>();
        await using var updateContext = m_DbFactory.CreateDbContext<UserDbContext>();

        var profiles = await deleteContext.UserProfiles
            .OrderBy(profile => profile.ProfileId)
            .ToListAsync();

        var concurrentUpdate = await updateContext.UserProfiles
            .SingleAsync(profile => profile.ProfileId == 2);
        concurrentUpdate.LToken = "rotated-profile-two";
        concurrentUpdate.LastCheckIn = new DateTime(2026, 9, 7, 4, 5, 6, DateTimeKind.Utc);
        await updateContext.SaveChangesAsync();

        await deleteContext.DeleteAndReindexProfilesAsync(profiles[0], profiles);

        var stored = await ReadProfilesAsync();
        Assert.Multiple(() =>
        {
            Assert.That(stored.Select(profile => profile.ProfileId), Is.EqualTo([1, 2]));
            Assert.That(stored[0].LtUid, Is.EqualTo(222));
            Assert.That(stored[0].LToken, Is.EqualTo("rotated-profile-two"));
            Assert.That(stored[0].LastCheckIn, Is.EqualTo(concurrentUpdate.LastCheckIn));
            Assert.That(stored[1].LtUid, Is.EqualTo(333));
            Assert.That(stored[1].LToken, Is.EqualTo("cipher-three"));
        });
    }

    private async Task<List<UserProfileModel>> ReadProfilesAsync()
    {
        await using var context = m_DbFactory.CreateDbContext<UserDbContext>();
        return await context.UserProfiles
            .AsNoTracking()
            .OrderBy(profile => profile.ProfileId)
            .ToListAsync();
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
