using Mehrak.Domain.Auth;
using Mehrak.Infrastructure.Auth;
using Mehrak.Infrastructure.Auth.Entities;
using Mehrak.Infrastructure.Auth.Services;
using Mehrak.Infrastructure.Tests.TestUtils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mehrak.Infrastructure.Tests.Auth.Services;

[TestFixture]
[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
internal sealed class DashboardSessionServiceTests : IDisposable
{
    private readonly TestDbContextFactory m_DbFactory = new();
    private DashboardSessionService m_Service = null!;

    public void Dispose()
    {
        m_DbFactory.Dispose();
    }

    private void SetupService()
    {
        m_Service = new DashboardSessionService(
            m_DbFactory.CreateDbContext<DashboardAuthDbContext>(),
            NullLogger<DashboardSessionService>.Instance);
    }

    private DashboardAuthDbContext CreateContext()
    {
        return m_DbFactory.CreateDbContext<DashboardAuthDbContext>();
    }

    private static DashboardSession CreateSession(
        string token = "abc123token0000000000000000001",
        long discordId = 123456789L,
        string? accessToken = "test-access-token",
        DateTime? expiresAt = null,
        DateTime? lastTokenValidation = null,
        string? loginIp = "127.0.0.1",
        string? userAgent = "TestAgent/1.0",
        string? location = null)
    {
        return new DashboardSession
        {
            Token = token,
            DiscordId = discordId,
            AccessToken = accessToken,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(7),
            LastTokenValidation = lastTokenValidation,
            LoginIp = loginIp,
            UserAgent = userAgent,
            Location = location
        };
    }

    #region CreateSessionAsync

    [Test]
    public async Task CreateSessionAsync_ValidInput_PersistsSession()
    {
        SetupService();

        await m_Service.CreateSessionAsync("token000000000000000000000000001", 100L, "access-token", "192.168.1.1", "Mozilla/5.0", "US");

        await using var ctx = CreateContext();
        var session = await ctx.DashboardSessions.FirstOrDefaultAsync(s => s.Token == "token000000000000000000000000001");

        Assert.That(session, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(session!.DiscordId, Is.EqualTo(100L));
            Assert.That(session.AccessToken, Is.EqualTo("access-token"));
            Assert.That(session.LoginIp, Is.EqualTo("192.168.1.1"));
            Assert.That(session.UserAgent, Is.EqualTo("Mozilla/5.0"));
            Assert.That(session.Location, Is.EqualTo("US"));
            Assert.That(session.ExpiresAt, Is.GreaterThan(DateTime.UtcNow.AddDays(6)));
        });
    }

    [Test]
    public async Task CreateSessionAsync_NullOptionalFields_PersistsWithNulls()
    {
        SetupService();

        await m_Service.CreateSessionAsync("token000000000000000000000000002", 200L, null, null, null, null);

        await using var ctx = CreateContext();
        var session = await ctx.DashboardSessions.FirstOrDefaultAsync(s => s.Token == "token000000000000000000000000002");

        Assert.That(session, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(session!.AccessToken, Is.Null);
            Assert.That(session.LoginIp, Is.Null);
            Assert.That(session.UserAgent, Is.Null);
            Assert.That(session.Location, Is.Null);
        });
    }

    #endregion

    #region GetSessionAsync

    [Test]
    public async Task GetSessionAsync_ExistingSession_ReturnsData()
    {
        SetupService();
        await using (var ctx = CreateContext())
        {
            ctx.DashboardSessions.Add(CreateSession(token: "token000000000000000000000000003", discordId: 42L, accessToken: "my-token"));
            await ctx.SaveChangesAsync();
        }

        var result = await m_Service.GetSessionAsync("token000000000000000000000000003");

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.DiscordUserId, Is.EqualTo(42L));
            Assert.That(result.AccessToken, Is.EqualTo("my-token"));
            Assert.That(result.LoginIp, Is.EqualTo("127.0.0.1"));
            Assert.That(result.UserAgent, Is.EqualTo("TestAgent/1.0"));
        });
    }

    [Test]
    public async Task GetSessionAsync_NonExistentToken_ReturnsNull()
    {
        SetupService();

        var result = await m_Service.GetSessionAsync("nonexistent000000000000000000000");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetSessionAsync_ExpiredSession_ReturnsNull()
    {
        SetupService();
        await using (var ctx = CreateContext())
        {
            ctx.DashboardSessions.Add(CreateSession(
                token: "token000000000000000000000000004",
                expiresAt: DateTime.UtcNow.AddSeconds(-1)));
            await ctx.SaveChangesAsync();
        }

        var result = await m_Service.GetSessionAsync("token000000000000000000000000004");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetSessionAsync_NullLastTokenValidation_FallsBackToCreatedAt()
    {
        SetupService();
        var createdAt = new DateTime(2025, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        await using (var ctx = CreateContext())
        {
            var session = CreateSession(token: "token000000000000000000000000005", lastTokenValidation: null);
            session.CreatedAt = createdAt;
            ctx.DashboardSessions.Add(session);
            await ctx.SaveChangesAsync();
        }

        var result = await m_Service.GetSessionAsync("token000000000000000000000000005");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.LastTokenValidation, Is.EqualTo(createdAt));
    }

    [Test]
    public async Task GetSessionAsync_WithLastTokenValidation_ReturnsValidationTime()
    {
        SetupService();
        var validationTime = new DateTime(2025, 6, 10, 12, 0, 0, DateTimeKind.Utc);
        await using (var ctx = CreateContext())
        {
            ctx.DashboardSessions.Add(CreateSession(
                token: "token000000000000000000000000006",
                lastTokenValidation: validationTime));
            await ctx.SaveChangesAsync();
        }

        var result = await m_Service.GetSessionAsync("token000000000000000000000000006");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.LastTokenValidation, Is.EqualTo(validationTime));
    }

    #endregion

    #region RefreshSessionAsync

    [Test]
    public async Task RefreshSessionAsync_ValidSession_ExtendsExpiry()
    {
        SetupService();
        var originalExpiry = DateTime.UtcNow.AddHours(1);
        await using (var ctx = CreateContext())
        {
            ctx.DashboardSessions.Add(CreateSession(token: "token000000000000000000000000007", expiresAt: originalExpiry));
            await ctx.SaveChangesAsync();
        }

        await m_Service.RefreshSessionAsync("token000000000000000000000000007");

        await using var verifyCtx = CreateContext();
        var session = await verifyCtx.DashboardSessions.FirstAsync(s => s.Token == "token000000000000000000000000007");
        Assert.That(session.ExpiresAt, Is.GreaterThan(originalExpiry));
    }

    [Test]
    public async Task RefreshSessionAsync_ExpiredSession_NoOp()
    {
        SetupService();
        await using (var ctx = CreateContext())
        {
            ctx.DashboardSessions.Add(CreateSession(
                token: "token000000000000000000000000008",
                expiresAt: DateTime.UtcNow.AddSeconds(-10)));
            await ctx.SaveChangesAsync();
        }

        Assert.DoesNotThrowAsync(() => m_Service.RefreshSessionAsync("token000000000000000000000000008"));
    }

    [Test]
    public async Task RefreshSessionAsync_NonExistentSession_NoOp()
    {
        SetupService();

        Assert.DoesNotThrowAsync(() => m_Service.RefreshSessionAsync("nonexistent000000000000000000000"));
    }

    #endregion

    #region InvalidateSessionAsync

    [Test]
    public async Task InvalidateSessionAsync_ExistingSession_DeletesFromDb()
    {
        SetupService();
        await using (var ctx = CreateContext())
        {
            ctx.DashboardSessions.Add(CreateSession(token: "token000000000000000000000000009"));
            await ctx.SaveChangesAsync();
        }

        await m_Service.InvalidateSessionAsync("token000000000000000000000000009");

        await using var verifyCtx = CreateContext();
        var session = await verifyCtx.DashboardSessions.FirstOrDefaultAsync(s => s.Token == "token000000000000000000000000009");
        Assert.That(session, Is.Null);
    }

    [Test]
    public async Task InvalidateSessionAsync_NonExistentSession_NoOp()
    {
        SetupService();

        Assert.DoesNotThrowAsync(() => m_Service.InvalidateSessionAsync("nonexistent000000000000000000000"));
    }

    [Test]
    public async Task InvalidateSessionAsync_ShortToken_HandlesSlice()
    {
        SetupService();
        await using (var ctx = CreateContext())
        {
            ctx.DashboardSessions.Add(CreateSession(token: "abc"));
            await ctx.SaveChangesAsync();
        }

        Assert.DoesNotThrowAsync(() => m_Service.InvalidateSessionAsync("abc"));

        await using var verifyCtx = CreateContext();
        var session = await verifyCtx.DashboardSessions.FirstOrDefaultAsync(s => s.Token == "abc");
        Assert.That(session, Is.Null);
    }

    #endregion

    #region InvalidateAllForUserAsync

    [Test]
    public async Task InvalidateAllForUserAsync_MultipleSessions_DeletesAll()
    {
        SetupService();
        await using (var ctx = CreateContext())
        {
            ctx.DashboardSessions.Add(CreateSession(token: "token000000000000000000000000010", discordId: 100L));
            ctx.DashboardSessions.Add(CreateSession(token: "token000000000000000000000000011", discordId: 100L));
            ctx.DashboardSessions.Add(CreateSession(token: "token000000000000000000000000012", discordId: 100L));
            ctx.DashboardSessions.Add(CreateSession(token: "token000000000000000000000000013", discordId: 200L));
            await ctx.SaveChangesAsync();
        }

        await m_Service.InvalidateAllForUserAsync(100L);

        await using var verifyCtx = CreateContext();
        var user100Sessions = await verifyCtx.DashboardSessions.Where(s => s.DiscordId == 100L).ToListAsync();
        var user200Sessions = await verifyCtx.DashboardSessions.Where(s => s.DiscordId == 200L).ToListAsync();

        Assert.Multiple(() =>
        {
            Assert.That(user100Sessions, Is.Empty);
            Assert.That(user200Sessions, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task InvalidateAllForUserAsync_NoSessions_NoOp()
    {
        SetupService();

        Assert.DoesNotThrowAsync(() => m_Service.InvalidateAllForUserAsync(999L));
    }

    #endregion

    #region TryClaimTokenValidationAsync

    [Test]
    public async Task TryClaimTokenValidationAsync_FirstValidationOfDay_ReturnsTrue()
    {
        SetupService();
        await using (var ctx = CreateContext())
        {
            ctx.DashboardSessions.Add(CreateSession(
                token: "token000000000000000000000000014",
                lastTokenValidation: DateTime.UtcNow.Date.AddDays(-1)));
            await ctx.SaveChangesAsync();
        }

        var result = await m_Service.TryClaimTokenValidationAsync("token000000000000000000000000014");

        Assert.That(result, Is.True);

        await using var verifyCtx = CreateContext();
        var session = await verifyCtx.DashboardSessions.FirstAsync(s => s.Token == "token000000000000000000000000014");
        Assert.That(session.LastTokenValidation, Is.Not.Null);
        Assert.That(session.LastTokenValidation!.Value.Date, Is.EqualTo(DateTime.UtcNow.Date));
    }

    [Test]
    public async Task TryClaimTokenValidationAsync_AlreadyValidatedToday_ReturnsFalse()
    {
        SetupService();
        await using (var ctx = CreateContext())
        {
            ctx.DashboardSessions.Add(CreateSession(
                token: "token000000000000000000000000015",
                lastTokenValidation: DateTime.UtcNow));
            await ctx.SaveChangesAsync();
        }

        var result = await m_Service.TryClaimTokenValidationAsync("token000000000000000000000000015");

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task TryClaimTokenValidationAsync_NullLastTokenValidation_ReturnsTrue()
    {
        SetupService();
        await using (var ctx = CreateContext())
        {
            ctx.DashboardSessions.Add(CreateSession(
                token: "token000000000000000000000000016",
                lastTokenValidation: null));
            await ctx.SaveChangesAsync();
        }

        var result = await m_Service.TryClaimTokenValidationAsync("token000000000000000000000000016");

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task TryClaimTokenValidationAsync_ExpiredSession_ReturnsFalse()
    {
        SetupService();
        await using (var ctx = CreateContext())
        {
            ctx.DashboardSessions.Add(CreateSession(
                token: "token000000000000000000000000017",
                expiresAt: DateTime.UtcNow.AddSeconds(-1)));
            await ctx.SaveChangesAsync();
        }

        var result = await m_Service.TryClaimTokenValidationAsync("token000000000000000000000000017");

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task TryClaimTokenValidationAsync_NonExistentSession_ReturnsFalse()
    {
        SetupService();

        var result = await m_Service.TryClaimTokenValidationAsync("nonexistent000000000000000000000");

        Assert.That(result, Is.False);
    }

    #endregion

    #region CleanupExpiredSessionsAsync

    [Test]
    public async Task CleanupExpiredSessionsAsync_ExpiredBeyondGrace_DeletesAndReturnsCount()
    {
        SetupService();
        await using (var ctx = CreateContext())
        {
            // Expired > 1 hour ago (should be deleted)
            ctx.DashboardSessions.Add(CreateSession(
                token: "token000000000000000000000000018",
                expiresAt: DateTime.UtcNow.AddHours(-2)));
            ctx.DashboardSessions.Add(CreateSession(
                token: "token000000000000000000000000019",
                expiresAt: DateTime.UtcNow.AddHours(-3)));
            // Valid session (should NOT be deleted)
            ctx.DashboardSessions.Add(CreateSession(
                token: "token000000000000000000000000020",
                expiresAt: DateTime.UtcNow.AddDays(1)));
            // Expired < 1 hour ago (within grace, should NOT be deleted)
            ctx.DashboardSessions.Add(CreateSession(
                token: "token000000000000000000000000021",
                expiresAt: DateTime.UtcNow.AddMinutes(-30)));
            await ctx.SaveChangesAsync();
        }

        var count = await m_Service.CleanupExpiredSessionsAsync();

        Assert.That(count, Is.EqualTo(2));

        await using var verifyCtx = CreateContext();
        var remaining = await verifyCtx.DashboardSessions.ToListAsync();
        Assert.That(remaining, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task CleanupExpiredSessionsAsync_NoExpiredSessions_ReturnsZero()
    {
        SetupService();
        await using (var ctx = CreateContext())
        {
            ctx.DashboardSessions.Add(CreateSession(
                token: "token000000000000000000000000022",
                expiresAt: DateTime.UtcNow.AddDays(1)));
            await ctx.SaveChangesAsync();
        }

        var count = await m_Service.CleanupExpiredSessionsAsync();

        Assert.That(count, Is.EqualTo(0));
    }

    [Test]
    public async Task CleanupExpiredSessionsAsync_EmptyDb_ReturnsZero()
    {
        SetupService();

        var count = await m_Service.CleanupExpiredSessionsAsync();

        Assert.That(count, Is.EqualTo(0));
    }

    #endregion
}
