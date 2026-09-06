using System.Security.Cryptography;
using Mehrak.Dashboard.Shared.Auth;
using Mehrak.Domain.Cache;
using Mehrak.Domain.Cache.Abstractions;
using Mehrak.Domain.Shared.Services;
using Mehrak.Infrastructure.Shared;
using Mehrak.Infrastructure.User;
using Mehrak.Infrastructure.User.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mehrak.Dashboard.Tests.Auth;

/// <summary>
/// Finding 8: Dashboard profile unlocks are bound to the login session and
/// the stored credential revision, carry an absolute TTL, and are revoked on
/// rotation, deletion, and logout across both client caches.
/// </summary>
[TestFixture]
public class DashboardProfileUnlockTests
{
    private sealed class FakeCacheService : ICacheService
    {
        public readonly Dictionary<string, object?> Store = new();
        public readonly List<string> Removed = new();
        public int SetCount;

        public Task SetAsync<T>(ICacheEntry<T> entry, CancellationToken cancellationToken = default)
        {
            Store[entry.Key] = entry.Value;
            SetCount++;
            return Task.CompletedTask;
        }

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            if (!Store.TryGetValue(key, out var value))
                return Task.FromResult(default(T?));
            // Mimics the JSON round-trip: an entry stored under an unexpected
            // shape (for example a legacy plaintext string) fails to read.
            return Task.FromResult((T?)value);
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            Store.Remove(key);
            Removed.Add(key);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeEncryptionService : IEncryptionService
    {
        public Func<string>? OnDecrypt;

        public string Encrypt(string plainText, string passphrase) => $"enc:{plainText}:{passphrase}";

        public string Decrypt(string cipherText, string passphrase)
        {
            OnDecrypt?.Invoke();
            if (passphrase == "correct")
                return $"plain-for:{cipherText}";
            throw new AuthenticationTagMismatchException();
        }

        public bool IsLegacyFormat(string cipherText) =>
            cipherText.StartsWith("legacy:", StringComparison.Ordinal);
    }

    private UserDbContext m_Db = null!;
    private FakeCacheService m_Cache = null!;
    private FakeEncryptionService m_Encryption = null!;
    private Mock<IPassphraseAttemptRateLimiter> m_Limiter = null!;
    private DashboardProfileAuthenticationService m_Service = null!;

    private const ulong UserId = 100UL;
    private const ulong LtUid = 111UL;
    private const string SessionA = "session-a";
    private const string SessionB = "session-b";
    private const string StoredCipher = "enc:token:correct";

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<UserDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        m_Db = new UserDbContext(options);
        m_Db.Users.Add(new UserModel
        {
            Id = (long)UserId,
            Timestamp = DateTime.UtcNow,
            Profiles =
            [
                new UserProfileModel
                {
                    UserId = (long)UserId,
                    ProfileId = 1,
                    LtUid = (long)LtUid,
                    LToken = StoredCipher
                }
            ]
        });
        m_Db.SaveChanges();

        m_Cache = new FakeCacheService();
        m_Encryption = new FakeEncryptionService();
        m_Limiter = new Mock<IPassphraseAttemptRateLimiter>();
        // Finding 12: reservation succeeds by default; individual tests override for blocked paths.
        m_Limiter.Setup(x => x.TryReserveAttemptAsync(It.IsAny<ulong>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid().ToString());
        m_Limiter.Setup(x => x.ReleaseReservationAsync(It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        m_Service = new DashboardProfileAuthenticationService(
            m_Db, m_Encryption, m_Cache,
            Mock.Of<ILogger<DashboardProfileAuthenticationService>>(),
            m_Limiter.Object);
    }

    [TearDown]
    public void TearDown() => m_Db.Dispose();

    [Test]
    public async Task Authenticate_FreshPassphrase_CachesTicketAndHitDoesNotRewrite()
    {
        var first = await m_Service.AuthenticateAsync(UserId, 1, "correct", TestContext.CurrentContext.CancellationToken, SessionA);

        Assert.That(first.Status, Is.EqualTo(DashboardAuthStatus.Success));
        Assert.That(m_Cache.SetCount, Is.EqualTo(1));

        var second = await m_Service.AuthenticateAsync(UserId, 1, null, TestContext.CurrentContext.CancellationToken, SessionA);

        Assert.That(second.Status, Is.EqualTo(DashboardAuthStatus.Success));
        Assert.That(second.LToken, Is.EqualTo(first.LToken));
        // Absolute TTL: a hit never refreshes or rewrites the entry.
        Assert.That(m_Cache.SetCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Authenticate_DifferentSession_DropsStaleTicketAndRequiresPassphrase()
    {
        await m_Service.AuthenticateAsync(UserId, 1, "correct", TestContext.CurrentContext.CancellationToken, SessionA);
        var key = CacheKeys.DashboardLToken(UserId, LtUid);
        Assert.That(m_Cache.Store, Contains.Key(key));

        var result = await m_Service.AuthenticateAsync(UserId, 1, null, TestContext.CurrentContext.CancellationToken, SessionB);

        Assert.That(result.Status, Is.EqualTo(DashboardAuthStatus.PassphraseRequired));
        Assert.That(m_Cache.Store, Does.Not.ContainKey(key));
        Assert.That(m_Cache.Removed, Contains.Item(key));
    }

    [Test]
    public async Task Authenticate_RotatedCredentials_DropsStaleTicket()
    {
        await m_Service.AuthenticateAsync(UserId, 1, "correct", TestContext.CurrentContext.CancellationToken, SessionA);

        // Rotation through the other client: stored ciphertext changes and
        // both caches are revoked.
        var row = await m_Db.UserProfiles.SingleAsync(p => p.UserId == (long)UserId);
        row.LToken = "enc:rotated-token:correct";
        await m_Db.SaveChangesAsync();
        await m_Service.RevokeAsync(UserId, LtUid);

        var result = await m_Service.AuthenticateAsync(UserId, 1, null, TestContext.CurrentContext.CancellationToken, SessionA);

        Assert.That(result.Status, Is.EqualTo(DashboardAuthStatus.PassphraseRequired));
    }

    [Test]
    public async Task Authenticate_RotationWithoutRevocation_StaleTicketStillDroppedByVersion()
    {
        await m_Service.AuthenticateAsync(UserId, 1, "correct", TestContext.CurrentContext.CancellationToken, SessionA);

        // Even if revocation were missed, the credential-version binding drops
        // the stale entry on read.
        var row = await m_Db.UserProfiles.SingleAsync(p => p.UserId == (long)UserId);
        row.LToken = "enc:rotated-token:correct";
        await m_Db.SaveChangesAsync();

        var result = await m_Service.AuthenticateAsync(UserId, 1, null, TestContext.CurrentContext.CancellationToken, SessionA);

        Assert.That(result.Status, Is.EqualTo(DashboardAuthStatus.PassphraseRequired));
        Assert.That(m_Cache.Store, Does.Not.ContainKey(CacheKeys.DashboardLToken(UserId, LtUid)));
    }

    [Test]
    public async Task Authenticate_RotationDuringAuthentication_DoesNotRepopulateCache()
    {
        // A rotation landing after decryption but before the cache write must
        // not repopulate the cache with stale credentials.
        m_Encryption.OnDecrypt = () =>
        {
            var row = m_Db.UserProfiles.Single(p => p.UserId == (long)UserId);
            row.LToken = "enc:rotated-token:correct";
            m_Db.SaveChanges();
            return string.Empty;
        };

        var result = await m_Service.AuthenticateAsync(UserId, 1, "correct", TestContext.CurrentContext.CancellationToken, SessionA);

        Assert.That(result.Status, Is.EqualTo(DashboardAuthStatus.Failure));
        Assert.That(m_Cache.Store, Is.Empty);
    }

    [Test]
    public async Task Authenticate_LegacyPlaintextEntry_DroppedAndReplaced()
    {
        // Legacy entries stored as plaintext under the same key can never
        // authenticate; the next passphrase authentication replaces them.
        var key = CacheKeys.DashboardLToken(UserId, LtUid);
        m_Cache.Store[key] = "plaintext-stale-token";

        var withoutPassphrase = await m_Service.AuthenticateAsync(UserId, 1, null, TestContext.CurrentContext.CancellationToken, SessionA);
        Assert.That(withoutPassphrase.Status, Is.EqualTo(DashboardAuthStatus.PassphraseRequired));
        Assert.That(m_Cache.Store, Does.Not.ContainKey(key));

        var withPassphrase = await m_Service.AuthenticateAsync(UserId, 1, "correct", TestContext.CurrentContext.CancellationToken, SessionA);
        Assert.That(withPassphrase.Status, Is.EqualTo(DashboardAuthStatus.Success));
        Assert.That(m_Cache.Store[key], Is.InstanceOf<DashboardUnlockTicket>());
    }

    [Test]
    public async Task Authenticate_LegacyCipherUpgrade_CachesUnderNewCredentialVersion()
    {
        var row = await m_Db.UserProfiles.SingleAsync(p => p.UserId == (long)UserId);
        row.LToken = "legacy:cipher";
        await m_Db.SaveChangesAsync();

        var result = await m_Service.AuthenticateAsync(UserId, 1, "correct", TestContext.CurrentContext.CancellationToken, SessionA);

        Assert.That(result.Status, Is.EqualTo(DashboardAuthStatus.Success));
        var ticket = (DashboardUnlockTicket)m_Cache.Store[CacheKeys.DashboardLToken(UserId, LtUid)]!;
        Assert.That(ticket.CredentialHash, Is.EqualTo(DashboardProfileAuthenticationService.ComputeHash(row.LToken)));

        var hit = await m_Service.AuthenticateAsync(UserId, 1, null, TestContext.CurrentContext.CancellationToken, SessionA);
        Assert.That(hit.Status, Is.EqualTo(DashboardAuthStatus.Success));
    }

    [Test]
    public async Task Authenticate_BlockedReservation_ReturnsRateLimitedWithoutDecrypting()
    {
        // Finding 12: the atomic reservation blocks before expensive PBKDF2 work.
        var decryptedCalled = false;
        m_Encryption.OnDecrypt = () => { decryptedCalled = true; return string.Empty; };
        m_Limiter.Setup(x => x.TryReserveAttemptAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await m_Service.AuthenticateAsync(UserId, 1, "wrong", TestContext.CurrentContext.CancellationToken, SessionA);

        Assert.That(result.Status, Is.EqualTo(DashboardAuthStatus.RateLimited));
        Assert.That(decryptedCalled, Is.False);
    }

    [Test]
    public async Task Authenticate_WrongPassphrase_KeepsReservationWithoutRelease()
    {
        // Finding 12: the reservation itself is the failure record; no second write.
        var result = await m_Service.AuthenticateAsync(UserId, 1, "wrong", TestContext.CurrentContext.CancellationToken, SessionA);

        Assert.That(result.Status, Is.EqualTo(DashboardAuthStatus.InvalidPassphrase));
        m_Limiter.Verify(x => x.ReleaseReservationAsync(It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Authenticate_Success_ReleasesReservation()
    {
        // Finding 12: correct passphrases release so successes never consume failure quota.
        var result = await m_Service.AuthenticateAsync(UserId, 1, "correct", TestContext.CurrentContext.CancellationToken, SessionA);

        Assert.That(result.Status, Is.EqualTo(DashboardAuthStatus.Success));
        m_Limiter.Verify(x => x.ReleaseReservationAsync(UserId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task RevokeAsync_RemovesBothBotAndDashboardKeys()
    {
        var botKey = CacheKeys.BotLToken(UserId, LtUid);
        var dashboardKey = CacheKeys.DashboardLToken(UserId, LtUid);
        m_Cache.Store[botKey] = "bot-token";
        m_Cache.Store[dashboardKey] = new DashboardUnlockTicket("c", "s", "token");

        await m_Service.RevokeAsync(UserId, LtUid);

        Assert.That(m_Cache.Store, Does.Not.ContainKey(botKey));
        Assert.That(m_Cache.Store, Does.Not.ContainKey(dashboardKey));
    }

    [Test]
    public async Task RevokeAllAsync_RemovesBothKeysForEveryProfile()
    {
        const ulong secondLtUid = 222UL;
        m_Db.UserProfiles.Add(new UserProfileModel
        {
            UserId = (long)UserId,
            ProfileId = 2,
            LtUid = (long)secondLtUid,
            LToken = "enc:other:correct"
        });
        await m_Db.SaveChangesAsync();

        var keys = new[]
        {
            CacheKeys.BotLToken(UserId, LtUid),
            CacheKeys.DashboardLToken(UserId, LtUid),
            CacheKeys.BotLToken(UserId, secondLtUid),
            CacheKeys.DashboardLToken(UserId, secondLtUid)
        };
        foreach (var key in keys)
            m_Cache.Store[key] = "token";

        await m_Service.RevokeAllAsync(UserId);

        Assert.That(m_Cache.Store, Is.Empty);
    }
}
