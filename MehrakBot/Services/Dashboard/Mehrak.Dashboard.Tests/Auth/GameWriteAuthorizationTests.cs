using System.Security.Claims;
using Mehrak.Dashboard.Shared;
using Mehrak.Dashboard.Shared.Auth;
using Mehrak.Domain.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mehrak.Dashboard.Tests.Auth;

[TestFixture]
public class GameWriteAuthorizationTests
{
    private static ClaimsPrincipal CreatePrincipal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "TestAuth"));

    private static Claim Perm(string game) => new("perm", $"game_write:{game}");

    private static readonly Claim SuperAdmin = new(ClaimTypes.Role, "superadmin");

    #region HasGameWriteAccess

    [Test]
    public void HasGameWriteAccess_SuperAdmin_AllowsEveryGame()
    {
        var principal = CreatePrincipal(SuperAdmin);

        foreach (Game game in Enum.GetValues<Game>())
            Assert.That(GameAuthorization.HasGameWriteAccess(principal, game), Is.True, $"game: {game}");
    }

    [Test]
    public void HasGameWriteAccess_ExactGameClaim_AllowsOnlyThatGame()
    {
        var principal = CreatePrincipal(Perm("genshin"));

        Assert.That(GameAuthorization.HasGameWriteAccess(principal, Game.Genshin), Is.True);
        Assert.That(GameAuthorization.HasGameWriteAccess(principal, Game.HonkaiStarRail), Is.False);
        Assert.That(GameAuthorization.HasGameWriteAccess(principal, Game.ZenlessZoneZero), Is.False);
        Assert.That(GameAuthorization.HasGameWriteAccess(principal, Game.HonkaiImpact3), Is.False);
    }

    [Test]
    public void HasGameWriteAccess_ClaimValue_IsCaseInsensitive()
    {
        var principal = CreatePrincipal(new Claim("perm", "GAME_WRITE:Genshin"));

        Assert.That(GameAuthorization.HasGameWriteAccess(principal, Game.Genshin), Is.True);
    }

    [Test]
    public void HasGameWriteAccess_WrongClaimType_Denies()
    {
        var principal = CreatePrincipal(new Claim("role", "game_write:genshin"));

        Assert.That(GameAuthorization.HasGameWriteAccess(principal, Game.Genshin), Is.False);
    }

    [Test]
    public void HasGameWriteAccess_NoPermissions_Denies()
    {
        var principal = CreatePrincipal(new Claim("discord_id", "123"));

        Assert.That(GameAuthorization.HasGameWriteAccess(principal, Game.Genshin), Is.False);
    }

    [Test]
    public void HasGameWriteAccess_NullOrUnauthenticated_Denies()
    {
        Assert.That(GameAuthorization.HasGameWriteAccess(null, Game.Genshin), Is.False);

        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        Assert.That(GameAuthorization.HasGameWriteAccess(anonymous, Game.Genshin), Is.False);
    }

    #endregion

    #region Resource policy

    private static IAuthorizationService CreateAuthorizationService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationBuilder()
            .AddPolicy(GameAuthorization.Policy, policy => policy.AddRequirements(new GameWriteRequirement()));
        services.AddSingleton<IAuthorizationHandler, GameWriteAuthorizationHandler>();
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    [Test]
    public async Task GamePolicy_Contributor_AllowsOwnGameOnly()
    {
        var authorization = CreateAuthorizationService();
        var principal = CreatePrincipal(Perm("honkaistarrail"));

        var allowed = await authorization.AuthorizeAsync(principal, Game.HonkaiStarRail, GameAuthorization.Policy);
        var denied = await authorization.AuthorizeAsync(principal, Game.Genshin, GameAuthorization.Policy);

        Assert.That(allowed.Succeeded, Is.True);
        Assert.That(denied.Succeeded, Is.False);
    }

    [Test]
    public async Task GamePolicy_SuperAdmin_AllowsAnyGame()
    {
        var authorization = CreateAuthorizationService();
        var principal = CreatePrincipal(SuperAdmin);

        var result = await authorization.AuthorizeAsync(principal, Game.ZenlessZoneZero, GameAuthorization.Policy);

        Assert.That(result.Succeeded, Is.True);
    }

    #endregion

    #region Controller helper

    private sealed class TestGameController : GameWriteController
    {
        public Task<bool> CheckAsync(Game game) => AuthorizeGameWriteAsync(game);

        public IActionResult Deny(Game game) => GameWriteDenied(game);
    }

    private static TestGameController CreateController(ClaimsPrincipal principal, IAuthorizationService authorization)
    {
        var controller = new TestGameController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = principal,
                    RequestServices = new ServiceCollection()
                        .AddSingleton(authorization)
                        .BuildServiceProvider()
                }
            }
        };
        return controller;
    }

    [Test]
    public async Task AuthorizeGameWriteAsync_Contributor_AllowsOwnGameDeniesOthers()
    {
        var authorization = CreateAuthorizationService();
        var controller = CreateController(CreatePrincipal(Perm("genshin")), authorization);

        Assert.That(await controller.CheckAsync(Game.Genshin), Is.True);
        Assert.That(await controller.CheckAsync(Game.HonkaiStarRail), Is.False);
    }

    [Test]
    public async Task AuthorizeGameWriteAsync_SuperAdmin_AllowsAllGames()
    {
        var authorization = CreateAuthorizationService();
        var controller = CreateController(CreatePrincipal(SuperAdmin), authorization);

        Assert.That(await controller.CheckAsync(Game.Genshin), Is.True);
        Assert.That(await controller.CheckAsync(Game.HonkaiImpact3), Is.True);
    }

    [Test]
    public void GameWriteDenied_Returns403()
    {
        var controller = CreateController(CreatePrincipal(), CreateAuthorizationService());

        var result = controller.Deny(Game.Genshin);

        Assert.That(result, Is.InstanceOf<ObjectResult>());
        Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
    }

    #endregion
}
