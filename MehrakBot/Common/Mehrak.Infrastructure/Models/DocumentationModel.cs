using System.ComponentModel.DataAnnotations;
using Mehrak.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Mehrak.Infrastructure.Models;

[Index(nameof(Game))]
[Index(nameof(Name))]
public class DocumentationModel
{
    [Key]
    public Guid Id { get; set; } = Guid.CreateVersion7();

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    [Required]
    public Game Game { get; set; }

    public List<DocumentationParameter> Parameters { get; set; } = [];

    public List<string> Examples { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class DocumentationParameter
{
    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Type { get; set; } = "string";

    [MaxLength(200)]
    public string Description { get; set; } = string.Empty;

    public bool Required { get; set; }
}
