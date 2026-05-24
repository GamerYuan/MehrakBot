using Mehrak.Dashboard.Models;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mehrak.Dashboard.Controllers;

[ApiController]
[Route("release-notes")]
public sealed class ReleaseNotesController : ControllerBase
{
    private readonly ReleaseNoteDbContext m_DbContext;
    private readonly ILogger<ReleaseNotesController> m_Logger;

    public ReleaseNotesController(ReleaseNoteDbContext dbContext, ILogger<ReleaseNotesController> logger)
    {
        m_DbContext = dbContext;
        m_Logger = logger;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAllReleaseNotes()
    {
        var releases = await m_DbContext.ReleaseVersions
            .AsNoTracking()
            .OrderByDescending(r => r.DisplayOrder)
            .Select(r => new
            {
                r.Id,
                r.Version,
                r.Date,
                r.DisplayOrder,
                Sections = r.Sections.Select(s => new
                {
                    s.Name,
                    Notes = s.Notes.Select(n => new
                    {
                        n.Type,
                        n.Text
                    })
                })
            })
            .ToListAsync();

        return Ok(releases);
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetReleaseNote(Guid id)
    {
        var release = await m_DbContext.ReleaseVersions
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new
            {
                r.Id,
                r.Version,
                r.Date,
                r.DisplayOrder,
                Sections = r.Sections.Select(s => new
                {
                    s.Name,
                    Notes = s.Notes.Select(n => new
                    {
                        n.Type,
                        n.Text
                    })
                })
            })
            .FirstOrDefaultAsync();

        if (release is null)
            return NotFound(new { error = "Release version not found." });

        return Ok(release);
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CreateReleaseNote([FromBody] ReleaseVersionRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var trimmedVersion = request.Version.Trim().ReplaceLineEndings("");

        var existing = await m_DbContext.ReleaseVersions
            .AnyAsync(r => r.Version.ToLower() == trimmedVersion.ToLower());

        if (existing)
            return Conflict(new { error = "Release version already exists." });

        var release = new ReleaseVersionModel
        {
            Version = trimmedVersion,
            Date = request.Date?.Trim().ReplaceLineEndings("") ?? string.Empty,
            DisplayOrder = request.DisplayOrder,
            Sections = [.. request.Sections.Select(s => new ReleaseNoteSection
            {
                Name = s.Name.Trim().ReplaceLineEndings(""),
                Notes = [.. s.Notes.Select(n => new ReleaseNoteEntry
                {
                    Type = n.Type.Trim().ToLowerInvariant().ReplaceLineEndings(""),
                    Text = n.Text.Trim().ReplaceLineEndings("")
                })]
            })],
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        m_DbContext.ReleaseVersions.Add(release);
        await m_DbContext.SaveChangesAsync();

        m_Logger.LogInformation("Created release version {Version}", release.Version);

        return Ok(new { id = release.Id });
    }

    [HttpPut("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> UpdateReleaseNote(Guid id, [FromBody] ReleaseVersionRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var release = await m_DbContext.ReleaseVersions.FindAsync(id);
        if (release is null)
            return NotFound(new { error = "Release version not found." });

        var trimmedVersion = request.Version.Trim().ReplaceLineEndings("");

        var existing = await m_DbContext.ReleaseVersions
            .AnyAsync(r => r.Id != id && r.Version.ToLower() == trimmedVersion.ToLower());

        if (existing)
            return Conflict(new { error = "Release version already exists." });

        release.Version = trimmedVersion;
        release.Date = request.Date?.Trim().ReplaceLineEndings("") ?? string.Empty;
        release.DisplayOrder = request.DisplayOrder;
        release.Sections = [.. request.Sections.Select(s => new ReleaseNoteSection
        {
            Name = s.Name.Trim().ReplaceLineEndings(""),
            Notes = [.. s.Notes.Select(n => new ReleaseNoteEntry
            {
                Type = n.Type.Trim().ToLowerInvariant().ReplaceLineEndings(""),
                Text = n.Text.Trim().ReplaceLineEndings("")
            })]
        })];
        release.UpdatedAt = DateTime.UtcNow;

        await m_DbContext.SaveChangesAsync();

        m_Logger.LogInformation("Updated release version {Version}", release.Version);

        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> DeleteReleaseNote(Guid id)
    {
        var release = await m_DbContext.ReleaseVersions.FindAsync(id);
        if (release is null)
            return NotFound(new { error = "Release version not found." });

        m_DbContext.ReleaseVersions.Remove(release);
        await m_DbContext.SaveChangesAsync();

        m_Logger.LogInformation("Deleted release version {Version}", release.Version);

        return NoContent();
    }
}
