using Mehrak.Dashboard.Models;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mehrak.Dashboard.Controllers;

[ApiController]
[Authorize]
[Route("codes")]
public sealed class CodesController : ControllerBase
{
    private readonly ICodeRedeemRepository m_CodeRepository;
    private readonly ILogger<CodesController> m_Logger;

    public CodesController(ICodeRedeemRepository codeRepository, ILogger<CodesController> logger)
    {
        m_CodeRepository = codeRepository;
        m_Logger = logger;
    }

    [HttpPatch("add")]
    public async Task<IActionResult> AddCodes([FromBody] AddCodesRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (!TryParseGame(request.Game, out var game, out var errorResult))
            return errorResult!;

        if (!HasGameWriteAccess(request.Game))
            return Forbid();

        var normalized = NormalizeCodes(request.Codes);
        if (normalized.Count == 0)
            return BadRequest(new { error = "Codes list must contain at least one value." });

        m_Logger.LogInformation("Adding {Count} codes for game {Game}", normalized.Count, game);

        var payload = normalized.ToDictionary(code => code, _ => CodeStatus.Valid, StringComparer.OrdinalIgnoreCase);
        await m_CodeRepository.UpdateCodesAsync(game, payload);

        return NoContent();
    }

    [HttpDelete("remove")]
    public async Task<IActionResult> RemoveCodes([FromQuery] RemoveCodesRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (!TryParseGame(request.Game, out var game, out var errorResult))
            return errorResult!;

        if (!HasGameWriteAccess(request.Game))
            return Forbid();

        var normalized = NormalizeCodes(request.Codes);
        if (normalized.Count == 0)
            return BadRequest(new { error = "Codes list must contain at least one value." });

        m_Logger.LogInformation("Removing {Count} codes for game {Game}", normalized.Count, game);

        var payload = normalized.ToDictionary(code => code, _ => CodeStatus.Invalid, StringComparer.OrdinalIgnoreCase);
        await m_CodeRepository.UpdateCodesAsync(game, payload);

        return NoContent();
    }

    [HttpGet("list")]
    public async Task<IActionResult> ListCodes([FromQuery] string game)
    {
        if (!TryParseGame(game, out var parsedGame, out var errorResult))
            return errorResult!;

        if (!HasGameWriteAccess(game))
            return Forbid();

        var codes = await m_CodeRepository.GetCodesAsync(parsedGame);
        return Ok(new { game = parsedGame.ToString(), codes });
    }

    private static bool TryParseGame(string gameValue, out Game game, out IActionResult? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(gameValue) || !Enum.TryParse(gameValue, true, out game))
        {
            game = default;
            error = new BadRequestObjectResult(new { error = "Invalid game value." });
            return false;
        }

        return true;
    }

    private bool HasGameWriteAccess(string game)
    {
        if (User.IsInRole("superadmin"))
            return true;

        var normalized = game.Trim().ToLowerInvariant();
        var claimValue = $"game_write:{normalized}";
        return User.Claims.Any(c =>
            string.Equals(c.Type, "perm", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(c.Value, claimValue, StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> NormalizeCodes(IEnumerable<string> codes)
    {
        return [.. codes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Where(code => code.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }
}
