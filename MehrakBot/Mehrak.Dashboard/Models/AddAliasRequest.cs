using System.ComponentModel.DataAnnotations;

namespace Mehrak.Dashboard.Models;

public class AddAliasRequest
{
    [Required(AllowEmptyStrings = false)]
    public string Character { get; set; } = string.Empty;

    [Required, MinLength(1)]
    public IEnumerable<string> Aliases { get; set; } = [];
}
