using Mehrak.Infrastructure.Tests.TestUtils;
using Mehrak.Infrastructure.User;
using Mehrak.Infrastructure.User.Models;
using Mehrak.Infrastructure.User.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Mehrak.Infrastructure.Tests.User.Services;

[TestFixture]
[Parallelizable(ParallelScope.Self)]
internal sealed class UserCountTrackerServiceTests
{
    private TestDbContextFactory m_DbFactory = null!;
    private UserCountTrackerService m_Tracker = null!;

    [SetUp]
    public void SetUp()
    {
        m_DbFactory = new TestDbContextFactory();
        using (var db = m_DbFactory.CreateDbContext<UserDbContext>())
        {
            db.Users.AddRange(
                new UserModel
                {
                    Id = 1,
                    Timestamp = DateTime.UtcNow,
                    Profiles =
                    [
                        new UserProfileModel { ProfileId = 1, LtUid = 100, LToken = "token" }
                    ]
                },
                new UserModel
                {
                    Id = 2,
                    Timestamp = DateTime.UtcNow
                });
            db.SaveChanges();
        }

        var services = new ServiceCollection()
            .AddScoped(_ => m_DbFactory.CreateDbContext<UserDbContext>())
            .BuildServiceProvider();
        m_Tracker = new UserCountTrackerService(services.GetRequiredService<IServiceScopeFactory>());
    }

    [TearDown]
    public void TearDown() => m_DbFactory.Dispose();

    [Test]
    public async Task GetUserCountAsync_ReadsCurrentDatabaseState()
    {
        Assert.That(await m_Tracker.GetUserCountAsync(), Is.EqualTo(1));

        using var db = m_DbFactory.CreateDbContext<UserDbContext>();
        db.Users.Add(new UserModel
        {
            Id = 3,
            Timestamp = DateTime.UtcNow,
            Profiles =
            [
                new UserProfileModel { ProfileId = 1, LtUid = 300, LToken = "token" }
            ]
        });
        db.SaveChanges();

        Assert.That(await m_Tracker.GetUserCountAsync(), Is.EqualTo(2));
    }
}
