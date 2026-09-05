using System.Security.Claims;
using Mehrak.Domain.Shared.Enums;
using Microsoft.AspNetCore.Authorization;

namespace Mehrak.Dashboard.Shared.Auth;

/// <summary>
/// Resource requirement constraining a write to a single exact game.
/// Register <see cref="GameWriteAuthorizationHandler"/> and authorize with
/// <see cref="GameAuthorization.Policy"/>, passing the target <see cref="Game"/>
/// as the resource.
/// </summary>
public sealed class GameWriteRequirement : IAuthorizationRequirement { }

public sealed class GameWriteAuthorizationHandler : AuthorizationHandler<GameWriteRequirement, Game>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        GameWriteRequirement requirement,
        Game resource)
    {
        if (GameAuthorization.HasGameWriteAccess(context.User, resource))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}

public static class GameAuthorization
{
    public const string Policy = "RequireGameWriteForGame";
    public const string PermissionClaimType = "perm";
    public const string SuperAdminRole = "superadmin";

    /// <summary>
    /// Superadmins may write any game. Contributors may only write the exact
    /// game named by their <c>game_write:{game}</c> permission claim.
    /// </summary>
    public static bool HasGameWriteAccess(ClaimsPrincipal? principal, Game game)
    {
        if (principal?.Identity?.IsAuthenticated != true)
            return false;

        if (principal.IsInRole(SuperAdminRole))
            return true;

        var wanted = $"game_write:{game.ToString().ToLowerInvariant()}";
        return principal.HasClaim(c =>
            string.Equals(c.Type, PermissionClaimType, StringComparison.Ordinal) &&
            string.Equals(c.Value, wanted, StringComparison.OrdinalIgnoreCase));
    }
}
