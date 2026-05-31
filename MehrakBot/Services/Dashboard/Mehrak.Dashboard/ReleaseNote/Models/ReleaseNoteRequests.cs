using System.ComponentModel.DataAnnotations;

namespace Mehrak.Dashboard.ReleaseNote.Models;

public sealed class ReleaseVersionRequest
{
    [Required]
    [MaxLength(50)]
    public string Version { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Date { get; set; }

    public int DisplayOrder { get; set; }

    [Required]
    public List<ReleaseSectionRequest> Sections { get; set; } = [];
}

public sealed class ReleaseSectionRequest
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public List<ReleaseNoteRequest> Notes { get; set; } = [];
}

public sealed class ReleaseNoteRequest
{
    [Required]
    [MaxLength(50)]
    public string Type { get; set; } = string.Empty;

    [Required]
    [MaxLength(2000)]
    public string Text { get; set; } = string.Empty;
}
