using Mehrak.Dashboard.Models;
using Mehrak.Domain.Enums;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mehrak.Dashboard.Controllers;

[ApiController]
[Route("docs")]
public sealed class DocumentationController : ControllerBase
{
    private readonly DocumentationDbContext m_DbContext;
    private readonly ILogger<DocumentationController> m_Logger;

    public DocumentationController(DocumentationDbContext dbContext, ILogger<DocumentationController> logger)
    {
        m_DbContext = dbContext;
        m_Logger = logger;
    }

    [HttpGet("list")]
    [AllowAnonymous]
    public async Task<IActionResult> ListDocumentation([FromQuery] string? game)
    {
        IQueryable<DocumentationModel> query = m_DbContext.Documentations.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(game))
        {
            if (!Enum.TryParse<Game>(game, true, out var gameEnum))
                return BadRequest(new { error = "Invalid game parameter." });

            query = query.Where(d => d.Game == gameEnum);
        }

        var docs = await query
            .OrderBy(d => d.Game)
            .ThenBy(d => d.Name)
            .Select(d => new
            {
                d.Id,
                d.Name,
                d.Description,
                Game = d.Game.ToString(),
                d.CreatedAt,
                d.UpdatedAt
            })
            .ToListAsync();

        return Ok(docs);
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetDocumentation(Guid id)
    {
        var doc = await m_DbContext.Documentations
            .AsNoTracking()
            .Where(d => d.Id == id)
            .Select(d => new
            {
                d.Id,
                d.Name,
                d.Description,
                Game = d.Game.ToString(),
                d.Parameters,
                d.Examples,
                d.CreatedAt,
                d.UpdatedAt
            })
            .FirstOrDefaultAsync();

        if (doc is null)
            return NotFound(new { error = "Documentation not found." });

        return Ok(doc);
    }

    [HttpPost("add")]
    [Authorize]
    public async Task<IActionResult> AddDocumentation([FromBody] AddDocumentationRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (!Enum.TryParse<Game>(request.Game, true, out var gameEnum))
            return BadRequest(new { error = "Invalid game parameter." });

        if (!HasGameWriteAccess(request.Game))
            return Forbid();

        var existing = await m_DbContext.Documentations
            .AnyAsync(d => d.Name.ToLower() == request.Name.ToLower() && d.Game == gameEnum);

        if (existing)
            return Conflict(new { error = "Documentation with this name already exists for the specified game." });

        var doc = new DocumentationModel
        {
            Name = request.Name.Trim(),
            Description = request.Description.Trim(),
            Game = gameEnum,
            Parameters = [.. request.Parameters.Select(p => new DocumentationParameter
            {
                Name = p.Name.Trim(),
                Type = p.Type?.Trim() ?? "string",
                Description = p.Description?.Trim() ?? string.Empty,
                Required = p.Required
            })],
            Examples = [.. request.Examples.Select(e => new DocumentationExample
            {
                Command = e.Command.Trim(),
                Description = e.Description?.Trim() ?? string.Empty
            })]
        };

        m_DbContext.Documentations.Add(doc);
        await m_DbContext.SaveChangesAsync();

        m_Logger.LogInformation("Created documentation {Name} for game {Game}", doc.Name, doc.Game);

        return Ok(new { id = doc.Id });
    }

    [HttpPut("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> UpdateDocumentation(Guid id, [FromBody] UpdateDocumentationRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (!Enum.TryParse<Game>(request.Game, true, out var gameEnum))
            return BadRequest(new { error = "Invalid game parameter." });

        if (!HasGameWriteAccess(request.Game))
            return Forbid();

        var doc = await m_DbContext.Documentations.FindAsync(id);
        if (doc is null)
            return NotFound(new { error = "Documentation not found." });

        var existing = await m_DbContext.Documentations
            .AnyAsync(d => d.Id != id && d.Name.ToLower() == request.Name.ToLower() && d.Game == gameEnum);

        if (existing)
            return Conflict(new { error = "Documentation with this name already exists for the specified game." });

        doc.Name = request.Name.Trim();
        doc.Description = request.Description.Trim();
        doc.Game = gameEnum;
        doc.Parameters = [.. request.Parameters.Select(p => new DocumentationParameter
        {
            Name = p.Name.Trim(),
            Type = p.Type?.Trim() ?? "string",
            Description = p.Description?.Trim() ?? string.Empty,
            Required = p.Required
        })];
        doc.Examples = [.. request.Examples.Select(e => new DocumentationExample
        {
            Command = e.Command.Trim(),
            Description = e.Description?.Trim() ?? string.Empty
        })];
        doc.UpdatedAt = DateTime.UtcNow;

        await m_DbContext.SaveChangesAsync();

        m_Logger.LogInformation("Updated documentation {Name} for game {Game}", doc.Name, doc.Game);

        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> DeleteDocumentation(Guid id)
    {
        var doc = await m_DbContext.Documentations.FindAsync(id);
        if (doc is null)
            return NotFound(new { error = "Documentation not found." });

        if (!HasGameWriteAccess(doc.Game.ToString()))
            return Forbid();

        m_DbContext.Documentations.Remove(doc);
        await m_DbContext.SaveChangesAsync();

        m_Logger.LogInformation("Deleted documentation {Name} for game {Game}", doc.Name, doc.Game);

        return NoContent();
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
