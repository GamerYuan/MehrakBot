using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Mehrak.Infrastructure.Models;

[Index(nameof(Version), IsUnique = true)]
public class ReleaseVersionModel
{
    [Key]
    public Guid Id { get; set; } = Guid.CreateVersion7();

    [Required]
    [MaxLength(50)]
    public string Version { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Date { get; set; }

    public int DisplayOrder { get; set; }

    public List<ReleaseNoteSection> Sections { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class ReleaseNoteSection
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public List<ReleaseNoteEntry> Notes { get; set; } = [];
}

public class ReleaseNoteEntry
{
    [Required]
    [MaxLength(50)]
    public string Type { get; set; } = string.Empty;

    [Required]
    [MaxLength(2000)]
    public string Text { get; set; } = string.Empty;
}
