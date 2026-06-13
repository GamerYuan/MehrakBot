using Mehrak.Domain.Auth;
using Mehrak.Domain.Auth.Dtos;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Infrastructure.Auth;
using Mehrak.Infrastructure.Auth.Entities;
using Mehrak.Infrastructure.Auth.Services;
using Mehrak.Infrastructure.Tests.TestUtils;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Mehrak.Infrastructure.Tests.Auth.Services;

[TestFixture]
[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
internal sealed class DashboardAuthServiceTests : IDisposable
{
    private readonly TestDbContextFactory m_DbFactory = new();
    private readonly Mock<IDashboardSessionService> m_MockSessionService = new();
    private DashboardAuthService m_Service = null!;

    public void Dispose()
    {
        m_DbFactory.Dispose();
    }

    private void SetupService()
    {
        m_Service = new DashboardAuthService(
            m_DbFactory.CreateDbContext<DashboardAuthDbContext>(),
            m_MockSessionService.Object,
            NullLogger<DashboardAuthService>.Instance);
    }

    private DashboardAuthDbContext CreateContext()
    {
        return m_DbFactory.CreateDbContext<DashboardAuthDbContext>();
    }

    private async Task SeedPermissionsAsync(long discordId, params string[] permissions)
    {
        await using var ctx = CreateContext();
        foreach (var permission in permissions)
        {
            ctx.DashboardPermissions.Add(new DashboardPermission
            {
                DiscordId = discordId,
                Permission = permission
            });
        }
        await ctx.SaveChangesAsync();
    }

    #region LoginByDiscordAsync — Permissions

    [Test]
    public async Task LoginByDiscordAsync_WithSuperAdmin_ReturnsIsSuperAdminTrue()
    {
        SetupService();
        await SeedPermissionsAsync(100L, "superadmin");

        var result = await m_Service.LoginByDiscordAsync(100L, "user", null, null, null, null, null);

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.IsSuperAdmin, Is.True);
            Assert.That(result.IsRootUser, Is.False);
        });
    }

    [Test]
    public async Task LoginByDiscordAsync_WithRootUser_ReturnsIsRootUserTrue()
    {
        SetupService();
        await SeedPermissionsAsync(100L, "rootuser");

        var result = await m_Service.LoginByDiscordAsync(100L, "user", null, null, null, null, null);

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.IsRootUser, Is.True);
            Assert.That(result.IsSuperAdmin, Is.False);
        });
    }

    [Test]
    public async Task LoginByDiscordAsync_WithGameWritePermissions_ReturnsCorrectGames()
    {
        SetupService();
        await SeedPermissionsAsync(100L, "game_write:genshin", "game_write:honkaistarrail");

        var result = await m_Service.LoginByDiscordAsync(100L, "user", null, null, null, null, null);

        Assert.That(result.GameWritePermissions, Is.EquivalentTo(new[] { Game.Genshin, Game.HonkaiStarRail }));
    }

    [Test]
    public async Task LoginByDiscordAsync_CaseInsensitivePermissions_ParsesCorrectly()
    {
        SetupService();
        await SeedPermissionsAsync(100L, "SUPERADMIN");
        await SeedPermissionsAsync(100L, "Game_Write:Genshin");

        var result = await m_Service.LoginByDiscordAsync(100L, "user", null, null, null, null, null);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuperAdmin, Is.True);
            Assert.That(result.GameWritePermissions, Does.Contain(Game.Genshin));
        });
    }

    [Test]
    public async Task LoginByDiscordAsync_InvalidGameWritePermission_SkipsUnsupported()
    {
        SetupService();
        await SeedPermissionsAsync(100L, "game_write:InvalidGame");

        var result = await m_Service.LoginByDiscordAsync(100L, "user", null, null, null, null, null);

        Assert.That(result.GameWritePermissions, Is.Empty);
    }

    [Test]
    public async Task LoginByDiscordAsync_NoPermissions_ReturnsEmptyPermissions()
    {
        SetupService();

        var result = await m_Service.LoginByDiscordAsync(100L, "user", null, null, null, null, null);

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.IsSuperAdmin, Is.False);
            Assert.That(result.IsRootUser, Is.False);
            Assert.That(result.GameWritePermissions, Is.Empty);
        });
    }

    #endregion

    #region LoginByDiscordAsync — Session Management

    [Test]
    public async Task LoginByDiscordAsync_InvalidatesExistingSessionsBeforeCreating()
    {
        SetupService();

        await m_Service.LoginByDiscordAsync(100L, "user", null, "token", null, null, null);

        m_MockSessionService.Verify(s => s.InvalidateAllForUserAsync(100L, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task LoginByDiscordAsync_CreatesSessionWithCorrectParameters()
    {
        SetupService();

        await m_Service.LoginByDiscordAsync(100L, "user", "avatar", "access-token", "192.168.1.1", "Mozilla/5.0", "US");

        m_MockSessionService.Verify(s => s.CreateSessionAsync(
            It.IsAny<string>(),
            100L,
            "access-token",
            "192.168.1.1",
            "Mozilla/5.0",
            "US",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task LoginByDiscordAsync_ReturnsSucceededTrue()
    {
        SetupService();

        var result = await m_Service.LoginByDiscordAsync(100L, "user", null, null, null, null, null);

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.SessionToken, Is.Not.Null.And.Not.Empty);
            Assert.That(result.DiscordUserId, Is.EqualTo(100L));
        });
    }

    [Test]
    public async Task LoginByDiscordAsync_GeneratesUniqueSessionTokens()
    {
        SetupService();

        var result1 = await m_Service.LoginByDiscordAsync(100L, "user", null, null, null, null, null);
        var result2 = await m_Service.LoginByDiscordAsync(100L, "user", null, null, null, null, null);

        Assert.That(result1.SessionToken, Is.Not.EqualTo(result2.SessionToken));
    }

    #endregion

    #region InvalidateSessionAsync

    [Test]
    public async Task InvalidateSessionAsync_DelegatesToSessionService()
    {
        SetupService();

        await m_Service.InvalidateSessionAsync("test-token");

        m_MockSessionService.Verify(s => s.InvalidateSessionAsync("test-token", It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region GetAccessTokenAsync

    [Test]
    public async Task GetAccessTokenAsync_SessionExists_ReturnsToken()
    {
        SetupService();
        m_MockSessionService.Setup(s => s.GetSessionAsync("token123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardSessionData(100L, "my-access-token", DateTime.UtcNow, null, null, null));

        var result = await m_Service.GetAccessTokenAsync("token123");

        Assert.That(result, Is.EqualTo("my-access-token"));
    }

    [Test]
    public async Task GetAccessTokenAsync_SessionNotFound_ReturnsNull()
    {
        SetupService();
        m_MockSessionService.Setup(s => s.GetSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DashboardSessionData?)null);

        var result = await m_Service.GetAccessTokenAsync("token123");

        Assert.That(result, Is.Null);
    }

    #endregion
}
