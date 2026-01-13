using Mehrak.Dashboard.Models;
using Mehrak.Domain.Enums;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mehrak.Dashboard.Controllers;

[ApiController]
[Authorize]
[Route("codes")]
public sealed class CodesController : ControllerBase
{
    private readonly CodeRedeemDbContext m_CodeContext;
    private readonly ILogger<CodesController> m_Logger;

    public CodesController(CodeRedeemDbContext codeContext, ILogger<CodesController> logger)
    {
        m_CodeContext = codeContext;
        m_Logger = logger;
    }

    [HttpPatch("add")]
    public async Task<IActionResult> AddCodes([FromQuery] string game, [FromBody] AddCodesRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (!TryParseGame(game, out var parsedGame, out var errorResult))
            return errorResult!;

        if (!HasGameWriteAccess(game))
            return Forbid();

        var normalized = NormalizeCodes(request.Codes);
        if (normalized.Count == 0)
            return BadRequest(new { error = "Codes list must contain at least one value." });

        m_Logger.LogInformation("Adding {Count} codes for game {Game}", normalized.Count, parsedGame);

        var payload = normalized.ToDictionary(code => code, _ => CodeStatus.Valid, StringComparer.OrdinalIgnoreCase);
        await UpdateCodesAsync(parsedGame, payload);

        return NoContent();
    }

    [HttpDelete("remove")]
    public async Task<IActionResult> RemoveCodes([FromQuery] string game, [FromQuery] RemoveCodesRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (!TryParseGame(game, out var parsedGame, out var errorResult))
            return errorResult!;

        if (!HasGameWriteAccess(game))
            return Forbid();

        var normalized = NormalizeCodes(request.Codes);
        if (normalized.Count == 0)
            return BadRequest(new { error = "Codes list must contain at least one value." });

        m_Logger.LogInformation("Removing {Count} codes for game {Game}", normalized.Count, parsedGame);

        var payload = normalized.ToDictionary(code => code, _ => CodeStatus.Invalid, StringComparer.OrdinalIgnoreCase);
        await UpdateCodesAsync(parsedGame, payload);

        return NoContent();
    }

    [HttpGet("list")]
    public async Task<IActionResult> ListCodes([FromQuery] string game)
    {
        if (!TryParseGame(game, out var parsedGame, out var errorResult))
            return errorResult!;

        if (!HasGameWriteAccess(game))
            return Forbid();

        var codes = await m_CodeContext.Codes.AsNoTracking().Where(x => x.Game == parsedGame).Select(x => x.Code).ToListAsync();
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

    private async Task UpdateCodesAsync(Game gameName, Dictionary<string, CodeStatus> codes)
    {
        var incoming = codes.Select(x => x.Key).ToHashSet();

        var existingCodes = await m_CodeContext.Codes.Where(x => x.Game == gameName && incoming.Contains(x.Code)).ToListAsync();

        var expiredCodes = codes
            .Where(kvp => kvp.Value == CodeStatus.Invalid)
            .Select(kvp => kvp.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<CodeRedeemModel> codesToRemove = [];

        if (expiredCodes.Count > 0)
        {
            codesToRemove.AddRange(existingCodes.Where(x => expiredCodes.Contains(x.Code)));
            m_CodeContext.Codes.RemoveRange(codesToRemove);
        }

        var newValidCodes = codes
            .Where(kvp => kvp.Value == CodeStatus.Valid)
            .Select(kvp => kvp.Key)
            .Except(existingCodes.Select(x => x.Code), StringComparer.OrdinalIgnoreCase)
            .Select(x => new CodeRedeemModel
            {
                Game = gameName,
                Code = x
            })
            .ToList();

        if (newValidCodes.Count > 0)
        {
            m_CodeContext.Codes.AddRange(newValidCodes);
        }

        try
        {
            await m_CodeContext.SaveChangesAsync();
            m_Logger.LogInformation("Added {Count} new codes, removed {Removed} expired codes for game: {Game}.",
                newValidCodes.Count, codesToRemove.Count, gameName);
        }
        catch (DbUpdateException e)
        {
            m_Logger.LogError(e, "Failed to update Codes for game: {Game}", gameName);
        }
    }
}
