using Mehrak.Dashboard.Shared.Auth;
using Mehrak.Domain.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mehrak.Dashboard.Shared;

[ApiController]
public abstract class GameWriteController : ControllerBase
{
    protected static bool TryParseGame(string? input, out Game game, out string error)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            error = "Game parameter is required.";
            game = default;
            return false;
        }

        if (!Enum.TryParse(input, true, out game))
        {
            error = "Invalid game parameter.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Enforces the exact target game server-side: superadmins pass every game,
    /// contributors only the games named by their <c>game_write:{game}</c> claims.
    /// </summary>
    protected async Task<bool> AuthorizeGameWriteAsync(Game game)
    {
        var authorization = HttpContext.RequestServices.GetRequiredService<IAuthorizationService>();
        return (await authorization.AuthorizeAsync(User, game, GameAuthorization.Policy)).Succeeded;
    }

    protected IActionResult GameWriteDenied(Game game) =>
        StatusCode(StatusCodes.Status403Forbidden, new { error = $"Write access for game '{game}' is required." });
}
