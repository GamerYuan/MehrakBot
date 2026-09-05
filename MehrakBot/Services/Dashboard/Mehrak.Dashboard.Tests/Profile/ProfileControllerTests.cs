using System.Net;
using System.Security.Claims;
using System.Text;
using Mehrak.Dashboard.Profile;
using Mehrak.Dashboard.Profile.Models;
using Mehrak.Domain.Cache;
using Mehrak.Domain.Cache.Abstractions;
using Mehrak.Domain.Shared.Services;
using Mehrak.GameApi.GameRole;
using Mehrak.Infrastructure.Shared.Config;
using Mehrak.Infrastructure.User;
using Mehrak.Infrastructure.User.Models;
using Mehrak.Infrastructure.User.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;

namespace Mehrak.Dashboard.Tests.Profile;

/// <summary>
/// Finding 8: credential mutation and deletion revoke both the Bot and the
/// Dashboard credential caches; deleting everything handles every profile.
/// Uses the EF InMemory provider, which lacks ExecuteDeleteAsync/ExecuteUpdateAsync,
/// so these tests also guard the load-then-mutate implementation.
/// </summary>
[TestFixture]
public class ProfileControllerTests
{
    private sealed class FakeCacheService : ICacheService
    {
        public readonly Dictionary<string, object?> Store = new();

        public Task SetAsync<T>(ICacheEntry<T> entry, CancellationToken cancellationToken = default)
        {
            Store[entry.Key] = entry.Value;
            return Task.CompletedTask;
        }

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(Store.TryGetValue(key, out var value) ? (T?)value : default);

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            Store.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class StubGameRoleHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            const string json = """
                {"retcode":0,"message":"OK","data":{"list":[
                  {"game_biz":"hk4e_global","region":"os_usa","game_uid":"600000001",
                   "nickname":"Tester","level":60,"is_chosen":true,"region_name":"America"}
                ]}}
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private UserDbContext m_Db = null!;
    private FakeCacheService m_Cache = null!;
    private Mock<IEncryptionService> m_Encryption = null!;
    private ProfileController m_Controller = null!;

    private const ulong UserId = 100UL;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<UserDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            // The controller wraps deletes in an explicit transaction, which
            // is a no-op on the in-memory store.
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        m_Db = new UserDbContext(options);
        m_Cache = new FakeCacheService();
        m_Encryption = new Mock<IEncryptionService>();

        var database = new Mock<IDatabase>();
        database.Setup(d => d.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(1L);
        var multiplexer = new Mock<IConnectionMultiplexer>();
        multiplexer.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);
        var tracker = new UserCountTrackerService(
            Options.Create(new RedisConfig { InstanceName = "Test_" }), multiplexer.Object);

        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(new StubGameRoleHandler()));
        var gameRoleApi = new GameRoleApiService(
            httpFactory.Object, m_Cache, Mock.Of<ILogger<GameRoleApiService>>());

        m_Controller = new ProfileController(
            m_Db, m_Encryption.Object, m_Cache, tracker, gameRoleApi,
            Mock.Of<ILogger<ProfileController>>());

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("discord_id", UserId.ToString())], "TestAuth"))
        };
        m_Controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    [TearDown]
    public void TearDown() => m_Db.Dispose();

    private void SeedProfiles(params (int ProfileId, ulong LtUid)[] profiles)
    {
        var user = new UserModel
        {
            Id = (long)UserId,
            Timestamp = DateTime.UtcNow,
            Profiles = profiles.Select(p => new UserProfileModel
            {
                UserId = (long)UserId,
                ProfileId = p.ProfileId,
                LtUid = (long)p.LtUid,
                LToken = $"enc:token:{p.LtUid}"
            }).ToList()
        };
        m_Db.Users.Add(user);
        m_Db.SaveChanges();
        m_Db.ChangeTracker.Clear();
    }

    private static string[] BothKeys(ulong ltUid) =>
    [
        CacheKeys.BotLToken(UserId, ltUid),
        CacheKeys.DashboardLToken(UserId, ltUid)
    ];

    [Test]
    public async Task UpdateProfile_ClearsBothBotAndDashboardKeys()
    {
        const ulong ltUid = 111UL;
        SeedProfiles((1, ltUid));
        foreach (var key in BothKeys(ltUid))
            m_Cache.Store[key] = "stale-token";
        m_Encryption.Setup(e => e.Encrypt(It.IsAny<string>(), It.IsAny<string>()))
            .Returns("enc:rotated");

        var result = await m_Controller.UpdateProfile(1, new UpdateProfileRequest
        {
            LToken = "new-ltoken",
            Passphrase = "correct"
        });

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        Assert.That(m_Cache.Store, Does.Not.ContainKey(CacheKeys.BotLToken(UserId, ltUid)));
        Assert.That(m_Cache.Store, Does.Not.ContainKey(CacheKeys.DashboardLToken(UserId, ltUid)));
        var row = await m_Db.UserProfiles.SingleAsync(p => p.UserId == (long)UserId);
        Assert.That(row.LToken, Is.EqualTo("enc:rotated"));
    }

    [Test]
    public async Task DeleteProfile_ClearsBothBotAndDashboardKeys()
    {
        const ulong ltUid = 111UL;
        const ulong otherLtUid = 222UL;
        SeedProfiles((1, ltUid), (2, otherLtUid));
        foreach (var key in BothKeys(ltUid).Concat(BothKeys(otherLtUid)))
            m_Cache.Store[key] = "stale-token";

        var result = await m_Controller.DeleteProfile(1);

        Assert.That(result, Is.InstanceOf<NoContentResult>());
        Assert.That(m_Cache.Store, Does.Not.ContainKey(CacheKeys.BotLToken(UserId, ltUid)));
        Assert.That(m_Cache.Store, Does.Not.ContainKey(CacheKeys.DashboardLToken(UserId, ltUid)));
        // The other profile's unlocks are untouched.
        Assert.That(m_Cache.Store, Contains.Key(CacheKeys.BotLToken(UserId, otherLtUid)));
        Assert.That(m_Cache.Store, Contains.Key(CacheKeys.DashboardLToken(UserId, otherLtUid)));
    }

    [Test]
    public async Task DeleteAllProfiles_RemovesEveryProfileAndClearsEveryKey()
    {
        const ulong firstLtUid = 111UL;
        const ulong secondLtUid = 222UL;
        SeedProfiles((1, firstLtUid), (2, secondLtUid));
        var keys = BothKeys(firstLtUid).Concat(BothKeys(secondLtUid)).ToArray();
        foreach (var key in keys)
            m_Cache.Store[key] = "stale-token";

        var result = await m_Controller.DeleteAllProfiles();

        Assert.That(result, Is.InstanceOf<NoContentResult>());
        Assert.That(await m_Db.UserProfiles.CountAsync(), Is.EqualTo(0));
        foreach (var key in keys)
            Assert.That(m_Cache.Store, Does.Not.ContainKey(key), $"stale cache entry {key} was not revoked");
    }

    [Test]
    public async Task DeleteAllProfiles_NoProfiles_ReturnsNoContent()
    {
        var result = await m_Controller.DeleteAllProfiles();

        Assert.That(result, Is.InstanceOf<NoContentResult>());
    }
}
