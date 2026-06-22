using Mehrak.Domain.Shared.Enums;
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
}
