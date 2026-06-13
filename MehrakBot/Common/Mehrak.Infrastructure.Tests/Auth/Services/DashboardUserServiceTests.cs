using Mehrak.Domain.Auth;
using Mehrak.Domain.Auth.Dtos;
using Mehrak.Domain.Shared.Enums;
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
internal sealed class DashboardUserServiceTests : IDisposable
{
    private readonly TestDbContextFactory m_DbFactory = new();
    private DashboardUserService m_Service = null!;

    public void Dispose()
    {
        m_DbFactory.Dispose();
    }

    private void SetupService()
    {
        m_Service = new DashboardUserService(
            m_DbFactory.CreateDbContext<DashboardAuthDbContext>(),
            NullLogger<DashboardUserService>.Instance);
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

    #region GetDashboardUsersAsync

    [Test]
    public async Task GetDashboardUsersAsync_UsersWithSuperAdminOrGameWrite_ReturnsBoth()
    {
        SetupService();
        await SeedPermissionsAsync(100L, "superadmin");
        await SeedPermissionsAsync(200L, "game_write:genshin");

        var result = await m_Service.GetDashboardUsersAsync();

        Assert.That(result, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GetDashboardUsersAsync_UserWithOnlyRootUser_Excluded()
    {
        SetupService();
        await SeedPermissionsAsync(100L, "rootuser");

        var result = await m_Service.GetDashboardUsersAsync();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetDashboardUsersAsync_EmptyDb_ReturnsEmpty()
    {
        SetupService();

        var result = await m_Service.GetDashboardUsersAsync();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetDashboardUsersAsync_OrdersByDiscordUserId()
    {
        SetupService();
        await SeedPermissionsAsync(300L, "superadmin");
        await SeedPermissionsAsync(100L, "game_write:genshin");
        await SeedPermissionsAsync(200L, "superadmin");

        var result = await m_Service.GetDashboardUsersAsync();
        var ids = result.Select(u => u.DiscordUserId).ToList();

        Assert.That(ids, Is.EqualTo(new[] { "100", "200", "300" }));
    }

    #endregion

    #region GetDashboardUserByDiscordIdAsync

    [Test]
    public async Task GetDashboardUserByDiscordIdAsync_Found_ReturnsSummary()
    {
        SetupService();
        await SeedPermissionsAsync(100L, "superadmin", "game_write:genshin");

        var result = await m_Service.GetDashboardUserByDiscordIdAsync(100L);

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.DiscordUserId, Is.EqualTo("100"));
            Assert.That(result.IsSuperAdmin, Is.True);
            Assert.That(result.GameWritePermissions, Does.Contain(Game.Genshin));
        });
    }

    [Test]
    public async Task GetDashboardUserByDiscordIdAsync_NoPermissions_ReturnsNull()
    {
        SetupService();

        var result = await m_Service.GetDashboardUserByDiscordIdAsync(999L);

        Assert.That(result, Is.Null);
    }

    #endregion

    #region AddDashboardUserAsync

    [Test]
    public async Task AddDashboardUserAsync_ValidRequest_AddsPermissions()
    {
        SetupService();

        var result = await m_Service.AddDashboardUserAsync(new AddDashboardUserRequestDto
        {
            DiscordUserId = 100L,
            GameWritePermissions = [Game.Genshin]
        });

        Assert.That(result.Succeeded, Is.True);

        await using var ctx = CreateContext();
        var perms = await ctx.DashboardPermissions.Where(p => p.DiscordId == 100L).ToListAsync();
        Assert.That(perms, Has.Count.EqualTo(1));
        Assert.That(perms[0].Permission, Is.EqualTo("game_write:genshin"));
    }

    [Test]
    public async Task AddDashboardUserAsync_DuplicateUser_ReturnsError()
    {
        SetupService();
        await SeedPermissionsAsync(100L, "superadmin");

        var result = await m_Service.AddDashboardUserAsync(new AddDashboardUserRequestDto
        {
            DiscordUserId = 100L,
            GameWritePermissions = []
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error, Does.Contain("already"));
        });
    }

    [Test]
    public async Task AddDashboardUserAsync_InvalidDiscordId_ReturnsError()
    {
        SetupService();

        var result = await m_Service.AddDashboardUserAsync(new AddDashboardUserRequestDto
        {
            DiscordUserId = 0L,
            GameWritePermissions = []
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error, Does.Contain("positive"));
        });
    }

    #endregion

    #region UpdateDashboardUserByDiscordIdAsync

    [Test]
    public async Task UpdateDashboardUserByDiscordIdAsync_RootUser_ReturnsError()
    {
        SetupService();
        await SeedPermissionsAsync(100L, "rootuser");

        var result = await m_Service.UpdateDashboardUserByDiscordIdAsync(new UpdateDashboardUserRequestDto
        {
            DiscordUserId = 100L,
            IsSuperAdmin = true,
            GameWritePermissions = []
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error, Does.Contain("Root user"));
        });
    }

    [Test]
    public async Task UpdateDashboardUserByDiscordIdAsync_AddsNewPermissions_RemovesOld()
    {
        SetupService();
        await SeedPermissionsAsync(100L, "game_write:genshin", "game_write:wutheringwaves");

        var result = await m_Service.UpdateDashboardUserByDiscordIdAsync(new UpdateDashboardUserRequestDto
        {
            DiscordUserId = 100L,
            IsSuperAdmin = true,
            GameWritePermissions = [Game.HonkaiStarRail]
        });

        Assert.That(result.Succeeded, Is.True);

        await using var ctx = CreateContext();
        var perms = await ctx.DashboardPermissions.Where(p => p.DiscordId == 100L).Select(p => p.Permission).ToListAsync();
        Assert.That(perms, Is.EquivalentTo(new[] { "superadmin", "game_write:honkaistarrail" }));
    }

    [Test]
    public async Task UpdateDashboardUserByDiscordIdAsync_NonExistentUser_CreatesPermissions()
    {
        SetupService();

        var result = await m_Service.UpdateDashboardUserByDiscordIdAsync(new UpdateDashboardUserRequestDto
        {
            DiscordUserId = 999L,
            IsSuperAdmin = false,
            GameWritePermissions = [Game.Genshin]
        });

        Assert.That(result.Succeeded, Is.True);

        await using var ctx = CreateContext();
        var perms = await ctx.DashboardPermissions.Where(p => p.DiscordId == 999L).ToListAsync();
        Assert.That(perms, Has.Count.EqualTo(1));
    }

    #endregion

    #region RemoveDashboardUserByDiscordIdAsync

    [Test]
    public async Task RemoveDashboardUserByDiscordIdAsync_RootUser_ReturnsError()
    {
        SetupService();
        await SeedPermissionsAsync(100L, "rootuser");

        var result = await m_Service.RemoveDashboardUserByDiscordIdAsync(100L);

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error, Does.Contain("Root user"));
        });
    }

    [Test]
    public async Task RemoveDashboardUserByDiscordIdAsync_ValidUser_DeletesPermissions()
    {
        SetupService();
        await SeedPermissionsAsync(100L, "superadmin", "game_write:genshin");

        var result = await m_Service.RemoveDashboardUserByDiscordIdAsync(100L);

        Assert.That(result.Succeeded, Is.True);

        await using var ctx = CreateContext();
        var perms = await ctx.DashboardPermissions.Where(p => p.DiscordId == 100L).ToListAsync();
        Assert.That(perms, Is.Empty);
    }

    [Test]
    public async Task RemoveDashboardUserByDiscordIdAsync_NonExistentUser_ReturnsNotFound()
    {
        SetupService();

        var result = await m_Service.RemoveDashboardUserByDiscordIdAsync(999L);

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error, Does.Contain("not found"));
        });
    }

    #endregion

    #region IsRootUserAsync

    [Test]
    public async Task IsRootUserAsync_IsRoot_ReturnsTrue()
    {
        SetupService();
        await SeedPermissionsAsync(100L, "rootuser");

        var result = await m_Service.IsRootUserAsync(100L);

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task IsRootUserAsync_IsNotRoot_ReturnsFalse()
    {
        SetupService();
        await SeedPermissionsAsync(100L, "superadmin");

        var result = await m_Service.IsRootUserAsync(100L);

        Assert.That(result, Is.False);
    }

    #endregion
}
