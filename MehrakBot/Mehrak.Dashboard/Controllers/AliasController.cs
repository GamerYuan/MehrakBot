using Mehrak.Dashboard.Models;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mehrak.Dashboard.Controllers;

[ApiController]
[Authorize]
[Route("alias")]
public class AliasController : ControllerBase
{
    private readonly IAliasRepository m_AliasRepository;
    private readonly ILogger<AliasController> m_Logger;

    public AliasController(IAliasRepository aliasRepository, ILogger<AliasController> logger)
    {
        m_AliasRepository = aliasRepository;
        m_Logger = logger;
    }

    [HttpGet("list")]
    public async Task<IActionResult> ListAliases([FromQuery] string? game)
    {
        if (!TryParseGame(game, out var gameEnum, out var error))
            return BadRequest(new { error });

        m_Logger.LogInformation("Listing aliases for game {Game}", gameEnum);
        var aliases = await m_AliasRepository.GetAliasesAsync(gameEnum);

        Dictionary<string, List<string>> result = [];

        foreach (var alias in aliases)
        {
            if (!result.ContainsKey(alias.Value))
                result.Add(alias.Value, []);
            result[alias.Value].Add(alias.Key);
        }

        return Ok(result);
    }

    [HttpPatch("add")]
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

        var existing = await m_AliasRepository.GetAliasesAsync(gameEnum);
        var existingKeys = new HashSet<string>(existing.Keys, StringComparer.OrdinalIgnoreCase);
        var conflicts = normalizedAliases.Where(existingKeys.Contains).ToArray();
        if (conflicts.Length > 0)
            return Conflict(new { error = $"Aliases already exist: {string.Join(", ", conflicts)}" });

        var payload = normalizedAliases.ToDictionary(alias => alias, _ => characterName, StringComparer.OrdinalIgnoreCase);
        m_Logger.LogInformation("Adding {Count} aliases for character {Character} in game {Game}", normalizedAliases.Length,
            characterName, gameEnum);
        await m_AliasRepository.UpsertAliasAsync(gameEnum, payload);
        return NoContent();
    }

    [HttpDelete("delete")]
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
        await m_AliasRepository.DeleteAliasAsync(gameEnum, normalized);
        return NoContent();
    }

    private static bool TryParseGame(string? input, out Game game, out string error)
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
}
