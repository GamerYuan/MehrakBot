using System.ComponentModel.DataAnnotations;

namespace Mehrak.Dashboard.Models;

public class AddAliasRequest
{
    [Required]
    public string Character { get; set; } = string.Empty;

    [Required]
    public IEnumerable<string> Aliases { get; set; } = Array.Empty<string>();
}
