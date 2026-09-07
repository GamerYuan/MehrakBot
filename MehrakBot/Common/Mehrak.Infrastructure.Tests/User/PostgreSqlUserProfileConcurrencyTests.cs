using Mehrak.Infrastructure.User;
using Mehrak.Infrastructure.User.Extensions;
using Mehrak.Infrastructure.User.Models;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Mehrak.Infrastructure.Tests.User;

[TestFixture]
[NonParallelizable]
public class PostgreSqlUserProfileConcurrencyTests
{
    private PostgreSqlContainer m_Container = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        m_Container = new PostgreSqlBuilder("postgres:18.3")
            .WithDatabase("profile_concurrency_tests")
            .WithUsername("profile_tests")
            .WithPassword("profile_tests_password")
            .Build();
        await m_Container.StartAsync();

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown() => await m_Container.DisposeAsync();

    [SetUp]
    public async Task SetUp()
    {
        await using var context = CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE \"GameUids\", \"Regions\", \"UserProfiles\", \"Users\" RESTART IDENTITY CASCADE");
        await context.Users.AddAsync(CreateUser());
        await context.SaveChangesAsync();
    }

    [Test]
    public async Task TryCompareAndSwapLTokenAsync_RotationOnIndependentConnectionWins()
    {
        await using var loginContext = CreateContext();
        await using var rotationContext = CreateContext();
        await AssertIndependentConnectionsAsync(loginContext, rotationContext);

        var snapshot = await loginContext.UserProfiles
            .AsNoTracking()
            .SingleAsync(profile => profile.ProfileId == 1);
        var rotation = await rotationContext.UserProfiles
            .SingleAsync(profile => profile.ProfileId == 1);
        rotation.LToken = "rotated-cipher";
        rotation.LastCheckIn = new DateTime(2026, 9, 8, 1, 2, 3, DateTimeKind.Utc);
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
    public async Task TryCompareAndSwapLTokenAsync_TwoIndependentUpgradesOnlyOneWins()
    {
        await using var firstLoginContext = CreateContext();
        await using var secondLoginContext = CreateContext();
        await AssertIndependentConnectionsAsync(firstLoginContext, secondLoginContext);

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

    [Test]
    public async Task DeleteAndReindexProfilesAsync_PreservesConcurrentStateAndUniqueProfileIds()
    {
        await using var deleteContext = CreateContext();
        await using var updateContext = CreateContext();
        await AssertIndependentConnectionsAsync(deleteContext, updateContext);

        var profiles = await deleteContext.UserProfiles
            .OrderBy(profile => profile.ProfileId)
            .ToListAsync();
        var concurrentUpdate = await updateContext.UserProfiles
            .SingleAsync(profile => profile.ProfileId == 2);
        concurrentUpdate.LToken = "rotated-profile-two";
        concurrentUpdate.LastCheckIn = new DateTime(2026, 9, 8, 4, 5, 6, DateTimeKind.Utc);
        await updateContext.SaveChangesAsync();

        await deleteContext.DeleteAndReindexProfilesAsync(profiles[0], profiles);

        await using var verificationContext = CreateContext();
        var stored = await verificationContext.UserProfiles
            .AsNoTracking()
            .OrderBy(profile => profile.ProfileId)
            .ToListAsync();
        var uniqueIndexCount = await GetUniqueProfileIndexCountAsync(verificationContext);

        Assert.Multiple(() =>
        {
            Assert.That(uniqueIndexCount, Is.EqualTo(1));
            Assert.That(stored.Select(profile => profile.ProfileId), Is.EqualTo([1, 2]));
            Assert.That(stored.Select(profile => profile.ProfileId).Distinct().Count(), Is.EqualTo(2));
            Assert.That(stored[0].LtUid, Is.EqualTo(222));
            Assert.That(stored[0].LToken, Is.EqualTo("rotated-profile-two"));
            Assert.That(stored[0].LastCheckIn, Is.EqualTo(concurrentUpdate.LastCheckIn));
            Assert.That(stored[1].LtUid, Is.EqualTo(333));
            Assert.That(stored[1].LToken, Is.EqualTo("cipher-three"));
        });
    }

    private UserDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<UserDbContext>()
            .UseNpgsql(m_Container.GetConnectionString())
            .Options;
        return new UserDbContext(options);
    }

    private async Task<UserProfileModel> ReadProfileAsync()
    {
        await using var context = CreateContext();
        return await context.UserProfiles
            .AsNoTracking()
            .SingleAsync(profile => profile.ProfileId == 1);
    }

    private static async Task AssertIndependentConnectionsAsync(
        UserDbContext firstContext,
        UserDbContext secondContext)
    {
        var backendPids = await Task.WhenAll(
            GetBackendProcessIdAsync(firstContext),
            GetBackendProcessIdAsync(secondContext));
        Assert.That(backendPids[0], Is.Not.EqualTo(backendPids[1]));
    }

    private static async Task<int> GetBackendProcessIdAsync(UserDbContext context)
    {
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT pg_backend_pid()";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<int> GetUniqueProfileIndexCountAsync(UserDbContext context)
    {
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM pg_indexes
            WHERE schemaname = current_schema()
              AND tablename = 'UserProfiles'
              AND indexname = 'IX_UserProfiles_UserId_ProfileId'
              AND indexdef LIKE '%UNIQUE%'
            """;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
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
