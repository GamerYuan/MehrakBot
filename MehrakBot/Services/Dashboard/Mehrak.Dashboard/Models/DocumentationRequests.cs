using System.ComponentModel.DataAnnotations;

namespace Mehrak.Dashboard.Models;

public sealed class DocumentationRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    [Required]
    public string Game { get; set; } = string.Empty;

    [Required]
    public List<DocumentationParameterRequest> Parameters { get; set; } = [];

    [Required]
    public List<string> Examples { get; set; } = [];
}

public sealed class DocumentationParameterRequest
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
