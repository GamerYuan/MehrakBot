using Mehrak.Dashboard.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Mehrak.Dashboard.Controllers;

[ApiController]
[Route("release-notes")]
public sealed class ReleaseNotesController : ControllerBase
{
    private readonly ReleaseNoteDbContext m_DbContext;
    private readonly ICacheService m_CacheService;
    private readonly ILogger<ReleaseNotesController> m_Logger;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(60);

    public ReleaseNotesController(
        ReleaseNoteDbContext dbContext,
        ICacheService cacheService,
        ILogger<ReleaseNotesController> logger)
    {
        m_DbContext = dbContext;
        m_CacheService = cacheService;
        m_Logger = logger;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAllReleaseNotes()
    {
        var cached = await m_CacheService.GetAsync<List<ReleaseNoteResponse>>(CacheKeys.ReleaseNotes, HttpContext.RequestAborted);
        if (cached is not null)
        {
            m_Logger.LogDebug("Serving release notes from cache");
            return Ok(cached);
        }

        var releases = await m_DbContext.ReleaseVersions
            .AsNoTracking()
            .OrderByDescending(r => r.DisplayOrder)
            .Select(r => new ReleaseNoteResponse
            {
                Id = r.Id,
                Version = r.Version,
                Date = r.Date,
                DisplayOrder = r.DisplayOrder,
                Sections = r.Sections.Select(s => new ReleaseNoteSectionResponse
                {
                    Name = s.Name,
                    Notes = s.Notes.Select(n => new ReleaseNoteEntryResponse
                    {
                        Type = n.Type,
                        Text = n.Text
                    }).ToList()
                }).ToList()
            })
            .ToListAsync();

        var cacheEntry = new CacheEntryBase<List<ReleaseNoteResponse>>(
            CacheKeys.ReleaseNotes, releases, CacheDuration);
        await m_CacheService.SetAsync(cacheEntry, HttpContext.RequestAborted);

        m_Logger.LogDebug("Populated release notes cache");
        return Ok(releases);
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetReleaseNote(Guid id)
    {
        var release = await m_DbContext.ReleaseVersions
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new ReleaseNoteResponse
            {
                Id = r.Id,
                Version = r.Version,
                Date = r.Date,
                DisplayOrder = r.DisplayOrder,
                Sections = r.Sections.Select(s => new ReleaseNoteSectionResponse
                {
                    Name = s.Name,
                    Notes = s.Notes.Select(n => new ReleaseNoteEntryResponse
                    {
                        Type = n.Type,
                        Text = n.Text
                    }).ToList()
                }).ToList()
            })
            .FirstOrDefaultAsync();

        if (release is null)
            return NotFound(new { error = "Release version not found." });

        return Ok(release);
    }

    [HttpPost]
    [Authorize(Roles = "superadmin")]
    public async Task<IActionResult> CreateReleaseNote([FromBody] ReleaseVersionRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (request.Sections.Count == 0)
            return BadRequest(new { error = "At least one section is required." });

        if (request.Sections.Any(s => s.Notes.Count == 0))
            return BadRequest(new { error = "Each section must have at least one note." });

        var trimmedVersion = request.Version.Trim().ReplaceLineEndings("");

        if (string.IsNullOrEmpty(trimmedVersion))
            return BadRequest(new { error = "Version is required." });

        var trimmedDate = request.Date?.Trim().ReplaceLineEndings("");
        if (!string.IsNullOrEmpty(trimmedDate) && !System.Text.RegularExpressions.Regex.IsMatch(trimmedDate, @"^\d{4}-\d{2}-\d{2}$"))
            return BadRequest(new { error = "Date must be in YYYY-MM-DD format." });

        var existing = await m_DbContext.ReleaseVersions
            .AnyAsync(r => r.Version.ToLower() == trimmedVersion.ToLower());

        if (existing)
            return Conflict(new { error = "Release version already exists." });

        var release = new ReleaseVersionModel
        {
            Version = trimmedVersion,
            Date = trimmedDate,
            DisplayOrder = request.DisplayOrder,
            Sections = [.. request.Sections.Select(s => new ReleaseNoteSection
            {
                Name = s.Name.Trim().ReplaceLineEndings(""),
                Notes = [.. s.Notes.Select(n => new ReleaseNoteEntry
                {
                    Type = n.Type.Trim().ToLowerInvariant().ReplaceLineEndings(""),
                    Text = n.Text.Trim()
                })]
            })],
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        m_DbContext.ReleaseVersions.Add(release);

        try
        {
            await m_DbContext.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx
                                          && pgEx.SqlState == "23505")
        {
            m_Logger.LogWarning(ex, "Duplicate release version {Version} detected at save", trimmedVersion);
            return Conflict(new { error = "Release version already exists." });
        }
        catch (DbUpdateException ex)
        {
            m_Logger.LogError(ex, "Error creating release version {Version}", trimmedVersion);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An error occurred while creating the release version." });
        }

        await TryInvalidateCacheAsync();
        m_Logger.LogInformation("Created release version {Version} and invalidated cache", release.Version);

        return CreatedAtAction(nameof(GetReleaseNote), new { id = release.Id }, new { id = release.Id });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "superadmin")]
    public async Task<IActionResult> UpdateReleaseNote(Guid id, [FromBody] ReleaseVersionRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (request.Sections.Count == 0)
            return BadRequest(new { error = "At least one section is required." });

        if (request.Sections.Any(s => s.Notes.Count == 0))
            return BadRequest(new { error = "Each section must have at least one note." });

        var release = await m_DbContext.ReleaseVersions.FindAsync(id);
        if (release is null)
            return NotFound(new { error = "Release version not found." });

        var trimmedVersion = request.Version.Trim().ReplaceLineEndings("");

        if (string.IsNullOrEmpty(trimmedVersion))
            return BadRequest(new { error = "Version is required." });

        var existing = await m_DbContext.ReleaseVersions
            .AnyAsync(r => r.Id != id && r.Version.ToLower() == trimmedVersion.ToLower());

        if (existing)
            return Conflict(new { error = "Release version already exists." });

        release.Version = trimmedVersion;

        var trimmedDate = request.Date?.Trim().ReplaceLineEndings("");
        if (!string.IsNullOrEmpty(trimmedDate) && !System.Text.RegularExpressions.Regex.IsMatch(trimmedDate, @"^\d{4}-\d{2}-\d{2}$"))
            return BadRequest(new { error = "Date must be in YYYY-MM-DD format." });

        release.Date = trimmedDate;
        release.DisplayOrder = request.DisplayOrder;
        release.Sections = [.. request.Sections.Select(s => new ReleaseNoteSection
        {
            Name = s.Name.Trim().ReplaceLineEndings(""),
            Notes = [.. s.Notes.Select(n => new ReleaseNoteEntry
            {
                Type = n.Type.Trim().ToLowerInvariant().ReplaceLineEndings(""),
                Text = n.Text.Trim()
            })]
        })];
        release.UpdatedAt = DateTime.UtcNow;

        try
        {
            await m_DbContext.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx
                                          && pgEx.SqlState == "23505")
        {
            m_Logger.LogWarning(ex, "Duplicate release version {Version} detected at update", trimmedVersion);
            return Conflict(new { error = "Release version already exists." });
        }
        catch (DbUpdateException ex)
        {
            m_Logger.LogError(ex, "Error updating release version {Version}", trimmedVersion);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An error occurred while updating the release version." });
        }

        await TryInvalidateCacheAsync();
        m_Logger.LogInformation("Updated release version {Version} and invalidated cache", release.Version);

        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "superadmin")]
    public async Task<IActionResult> DeleteReleaseNote(Guid id)
    {
        var release = await m_DbContext.ReleaseVersions.FindAsync(id);
        if (release is null)
            return NotFound(new { error = "Release version not found." });

        m_DbContext.ReleaseVersions.Remove(release);

        try
        {
            await m_DbContext.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            m_Logger.LogError(ex, "Error deleting release version {Version}", release.Version);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An error occurred while deleting the release version." });
        }

        await TryInvalidateCacheAsync();
        m_Logger.LogInformation("Deleted release version {Version} and invalidated cache", release.Version);

        return NoContent();
    }

    private async ValueTask TryInvalidateCacheAsync()
    {
        try
        {
            await m_CacheService.RemoveAsync(CacheKeys.ReleaseNotes, CancellationToken.None);
        }
        catch (Exception ex)
        {
            m_Logger.LogWarning(ex, "Failed to invalidate release notes cache, database update already completed");
        }
    }
}
