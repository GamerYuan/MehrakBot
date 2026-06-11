using Mehrak.Dashboard.Character.Models;
using Mehrak.Dashboard.Shared;
using Mehrak.Domain.Character;
using Mehrak.Domain.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mehrak.Dashboard.Character;

[ApiController]
[Route("alias")]
[Authorize]
public class AliasController : GameWriteController
{
    private readonly IAliasService m_AliasService;
    private readonly ILogger<AliasController> m_Logger;

    public AliasController(IAliasService aliasService, ILogger<AliasController> logger)
    {
        m_AliasService = aliasService;
        m_Logger = logger;
    }

    [AllowAnonymous]
    [HttpGet("list")]
    public async Task<IActionResult> ListAliases([FromQuery] string? game)
    {
        if (!TryParseGame(game, out var gameEnum, out var error))
            return BadRequest(new { error });

        m_Logger.LogInformation("Listing aliases for game {Game}", gameEnum);
        var aliases = m_AliasService.GetAliases(gameEnum);

        var result = aliases
            .GroupBy(x => x.Value)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Key).ToList());

        return Ok(result);
    }

    [HttpPatch("add")]
    [Authorize(Policy = "RequireGameWrite")]
    public async Task<IActionResult> AddAliases([FromQuery] string? game, [FromBody] AddAliasRequest request)
    {
        if (!TryParseGame(game, out var gameEnum, out var error))
            return BadRequest(new { error });

        if (!HasGameWriteAccess(game!))
            return Forbid();

        if (request == null || string.IsNullOrWhiteSpace(request.Character))
            return BadRequest(new { error = "Character name is required." });

        if (request.Aliases == null)
            return BadRequest(new { error = "Aliases payload is required." });

        var characterName = request.Character.ReplaceLineEndings("").Trim();
        if (characterName.Length == 0)
            return BadRequest(new { error = "Character name is required." });

        request.Character = characterName;

        var normalizedAliases = request.Aliases
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.ReplaceLineEndings("").Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedAliases.Length == 0)
            return BadRequest(new { error = "Aliases list must contain at least one alias." });

        var aliases = m_AliasService.GetAliases(gameEnum);
        var conflicts = normalizedAliases.Where(aliases.ContainsKey).ToArray();
        if (conflicts.Length > 0)
            return Conflict(new { error = $"Aliases already exist: {string.Join(", ", conflicts)}" });

        m_Logger.LogInformation("Adding {Count} aliases for character {Character} in game {Game}", normalizedAliases.Length,
            characterName, gameEnum);

        var newAliases = normalizedAliases.ToDictionary(a => a, _ => characterName);
        await m_AliasService.UpsertAliases(gameEnum, newAliases);

        return NoContent();
    }

    [HttpDelete("delete")]
    [Authorize(Policy = "RequireGameWrite")]
    public async Task<IActionResult> DeleteAlias([FromQuery] string? game, [FromQuery] string? alias)
    {
        if (!TryParseGame(game, out var gameEnum, out var error))
            return BadRequest(new { error });

        if (string.IsNullOrWhiteSpace(alias))
            return BadRequest(new { error = "Alias parameter is required." });

        if (!HasGameWriteAccess(game!))
            return Forbid();

        var normalized = alias.ReplaceLineEndings("").Trim();
        m_Logger.LogInformation("Deleting alias {Alias} for game {Game}", normalized, gameEnum);

        await m_AliasService.DeleteAlias(gameEnum, normalized);

        return NoContent();
    }
}
