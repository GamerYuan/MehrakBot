using Mehrak.Dashboard.Models;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Services.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mehrak.Dashboard.Controllers;

[ApiController]
[Authorize]
[Route("characters")]
public class CharactersController : ControllerBase
{
    private readonly ICharacterCacheService m_CharacterCacheService;
    private readonly ILogger<CharactersController> m_Logger;

    public CharactersController(ICharacterCacheService characterCacheService, ILogger<CharactersController> logger)
    {
        m_CharacterCacheService = characterCacheService;
        m_Logger = logger;
    }

    [HttpGet("list")]
    public async Task<IActionResult> ListCharacters([FromQuery] string? game)
    {
        if (!TryParseGame(game, out var gameEnum, out var error))
            return BadRequest(new { error });

        m_Logger.LogInformation("Listing characters for game {Game}", gameEnum);
        var characters = m_CharacterCacheService.GetCharacters(gameEnum);
        return Ok(characters);
    }

    [HttpPatch("add")]
    public async Task<IActionResult> AddCharacters([FromQuery] string? game, [FromBody] AddCharactersRequest request)
    {
        if (!TryParseGame(game, out var gameEnum, out var error))
            return BadRequest(new { error });

        if (request?.Characters == null)
            return BadRequest(new { error = "Characters payload is required." });

        var normalizedCharacters = request.Characters
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.ReplaceLineEndings("").Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedCharacters.Length == 0)
            return BadRequest(new { error = "Characters list must contain at least one name." });

        if (!HasGameWriteAccess(game!))
            return Forbid();

        m_Logger.LogInformation("Adding {Count} characters to game {Game}", normalizedCharacters.Length, gameEnum);
        await m_CharacterCacheService.UpsertCharacters(gameEnum, normalizedCharacters);

        return NoContent();
    }

    [HttpDelete("delete")]
    public async Task<IActionResult> DeleteCharacter([FromQuery] string? game, [FromQuery] string? character)
    {
        if (!TryParseGame(game, out var gameEnum, out var error))
            return BadRequest(new { error });

        if (string.IsNullOrWhiteSpace(character))
            return BadRequest(new { error = "Character parameter is required." });

        if (!HasGameWriteAccess(game!))
            return Forbid();

        var normalized = character.ReplaceLineEndings("").Trim();
        m_Logger.LogInformation("Deleting character {Character} from game {Game}", normalized, gameEnum);

        await m_CharacterCacheService.DeleteCharacter(gameEnum, normalized);

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

        if (!Enum.TryParse<Game>(input, true, out game))
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
