using Mehrak.Dashboard.Character.Models;
using Mehrak.Dashboard.Shared;
using Mehrak.Domain.Character;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Mehrak.Dashboard.Character;

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

        if (!await AuthorizeGameWriteAsync(gameEnum))
            return GameWriteDenied(gameEnum);

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

        var newAliases = normalizedAliases.ToDictionary(a => a, _ => characterName, StringComparer.OrdinalIgnoreCase);
        try
        {
            await m_AliasService.UpsertAliases(gameEnum, newAliases);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: "23505" })
        {
            m_Logger.LogWarning(exception, "Alias insert raced with another request for game {Game}", gameEnum);
            return Conflict(new { error = "One or more aliases were added by another request. Refresh and try again." });
        }
        catch (CacheSynchronizationException exception) when (exception.DatabaseCommitted)
        {
            m_Logger.LogError(exception, "Aliases were committed but cache refresh failed for {Game}", gameEnum);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Aliases were saved, but the alias cache is temporarily unavailable." });
        }
        catch (DbUpdateException exception)
        {
            m_Logger.LogError(exception, "Failed to add aliases for game {Game}", gameEnum);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Failed to save aliases. Please try again later." });
        }

        return NoContent();
    }

    [HttpDelete("delete")]
    [Authorize(Policy = "RequireGameWrite")]
    public async Task<IActionResult> DeleteAlias([FromQuery] string? game, [FromQuery] string? alias)
    {
        if (!TryParseGame(game, out var gameEnum, out var error))
            return BadRequest(new { error });

        if (!await AuthorizeGameWriteAsync(gameEnum))
            return GameWriteDenied(gameEnum);

        if (string.IsNullOrWhiteSpace(alias))
            return BadRequest(new { error = "Alias parameter is required." });

        var normalized = alias.ReplaceLineEndings("").Trim();
        m_Logger.LogInformation("Deleting alias {Alias} for game {Game}", normalized, gameEnum);

        try
        {
            await m_AliasService.DeleteAlias(gameEnum, normalized);
        }
        catch (CacheSynchronizationException exception) when (exception.DatabaseCommitted)
        {
            m_Logger.LogError(exception, "Alias deletion committed but cache refresh failed for {Game}", gameEnum);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Alias was deleted, but the alias cache is temporarily unavailable." });
        }
        catch (DbUpdateException exception)
        {
            m_Logger.LogError(exception, "Failed to delete alias for game {Game}", gameEnum);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Failed to delete alias. Please try again later." });
        }

        return NoContent();
    }
}
