using System.ComponentModel.DataAnnotations;
using Mehrak.Domain.Shared.Enums;

namespace Mehrak.Infrastructure.Character.Models;

public class AliasConflictModel
{
    [Key]
    public int Id { get; set; }

    public Game Game { get; set; }

    [MaxLength(20)]
    public string Alias { get; set; } = string.Empty;

    [MaxLength(20)]
    public string OriginalAlias { get; set; } = string.Empty;

    [MaxLength(100)]
    public string CharacterName { get; set; } = string.Empty;

    public int SourceAliasId { get; set; }

    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
}
