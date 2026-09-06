﻿using Mehrak.Domain.Auth;
using Mehrak.Domain.Auth.Dtos;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Infrastructure.Auth;
using Mehrak.Infrastructure.Auth.Entities;
using Mehrak.Infrastructure.Auth.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mehrak.Dashboard.Tests.Auth;

/// <summary>
/// Login/session creation must not race permission revocation. </summary>
[TestFixture]
public class LoginRevocationRaceTests
{
    private static DashboardAuthDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<DashboardAuthDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new DashboardAuthDbContext(options);
    }

    private static void SeedPermission(DashboardAuthDbContext db, long discordId, string permission)
    {
        db.DashboardPermissions.Add(new DashboardPermission { DiscordId = discordId, Permission = permission });
        db.SaveChanges();
    }

    [Test]
    public async Task LoginByDiscordAsync_RevocationRacingSessionCreation_IsNotReflectedInCookie()
    {
        using var db = CreateDb();
        const long discordId = 100L;
        SeedPermission(db, discordId, "game_write:genshin");

        // Simulate an administrator revocation landing after the login started but
        // before its permission snapshot is taken: the issued cookie must reflect
        // the post-revocation state, never the stale pre-revocation permissions.
        var mockSessions = new Mock<IDashboardSessionService>();
        mockSessions.Setup(s => s.InvalidateAllForUserAsync(discordId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mockSessions.Setup(s => s.CreateSessionAsync(
                It.IsAny<string>(), discordId, It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                var raced = db.DashboardPermissions
                    .Single(p => p.DiscordId == discordId && p.Permission == "game_write:genshin");
                db.DashboardPermissions.Remove(raced);
                db.SaveChanges();
            })
            .Returns(Task.CompletedTask);

        var auth = new DashboardAuthService(db, mockSessions.Object, Mock.Of<ILogger<DashboardAuthService>>());

        var result = await auth.LoginByDiscordAsync(discordId, "user", null, null, null, null, null);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.SessionToken, Is.Not.Null);
        Assert.That(result.GameWritePermissions, Does.Not.Contain(Game.Genshin));
    }

    [Test]
    public async Task LoginByDiscordAsync_ReturnsCurrentPermissions()
    {
        using var db = CreateDb();
        const long discordId = 101L;
        SeedPermission(db, discordId, "game_write:honkaistarrail");

        var mockSessions = new Mock<IDashboardSessionService>();
        var auth = new DashboardAuthService(db, mockSessions.Object, Mock.Of<ILogger<DashboardAuthService>>());

        var result = await auth.LoginByDiscordAsync(discordId, "user", null, null, null, null, null);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.GameWritePermissions, Is.EquivalentTo([Game.HonkaiStarRail]));
        mockSessions.Verify(s => s.InvalidateAllForUserAsync(discordId, It.IsAny<CancellationToken>()), Times.Once);
        mockSessions.Verify(s => s.CreateSessionAsync(
            It.IsAny<string>(), discordId, It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task UpdateDashboardUser_PermissionChange_InvalidatesSessions()
    {
        using var db = CreateDb();
        const long discordId = 102L;
        SeedPermission(db, discordId, "game_write:genshin");

        var mockSessions = new Mock<IDashboardSessionService>();
        var users = new DashboardUserService(db, mockSessions.Object, Mock.Of<ILogger<DashboardUserService>>());

        var result = await users.UpdateDashboardUserByDiscordIdAsync(new UpdateDashboardUserRequestDto
        {
            DiscordUserId = discordId,
            IsSuperAdmin = false,
            GameWritePermissions = []
        });

        Assert.That(result.Succeeded, Is.True);
        Assert.That(db.DashboardPermissions.Any(p => p.DiscordId == discordId), Is.False);
        mockSessions.Verify(s => s.InvalidateAllForUserAsync(discordId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task RemoveDashboardUser_PermissionRemoval_InvalidatesSessions()
    {
        using var db = CreateDb();
        const long discordId = 103L;
        SeedPermission(db, discordId, "game_write:genshin");

        var mockSessions = new Mock<IDashboardSessionService>();
        var users = new DashboardUserService(db, mockSessions.Object, Mock.Of<ILogger<DashboardUserService>>());

        var result = await users.RemoveDashboardUserByDiscordIdAsync(discordId);

        Assert.That(result.Succeeded, Is.True);
        mockSessions.Verify(s => s.InvalidateAllForUserAsync(discordId, It.IsAny<CancellationToken>()), Times.Once);
    }
}

